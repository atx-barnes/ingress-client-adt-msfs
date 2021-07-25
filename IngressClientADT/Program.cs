using Azure;
using Azure.Core.Pipeline;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace IngressClientADT
{
    public class SimvarRequest
    {
        public DEFINITION Def = DEFINITION.Dummy;
        public REQUEST Request = REQUEST.Dummy;
        public string Name { get; set; }
        public bool IsString { get; set; }
        public double Value = 0.0;
        public string sValue = null;
        public string Units { get; set; }
        public ITwinable Twin { get; set; }
        public bool Pending = true;
    };

    public enum DEFINITION { Dummy = 0 };
    public enum REQUEST { Dummy = 0, Struct1 };

    class Program
    {
        private static DigitalTwinIngressControlller digitalTwinController;
        private static MicrosoftFlightSimulatorConnection microsoftFlightSimulatorConnection;

        static void Main(string[] args)
        {
            digitalTwinController = new DigitalTwinIngressControlller("https://immersiveadtadthizi25q7e2.api.eus.digitaltwins.azure.net");
            Console.WriteLine("Press Enter to Connect to MSFS2020...");
            Console.ReadLine();

            microsoftFlightSimulatorConnection = new MicrosoftFlightSimulatorConnection(1000);
            microsoftFlightSimulatorConnection.Connect();
            microsoftFlightSimulatorConnection.OnUserAircraftCreated += digitalTwinController.Standup;
            microsoftFlightSimulatorConnection.OnSimulationObjectReceived += digitalTwinController.PublishTelemetry;
            microsoftFlightSimulatorConnection.OnSimulationExit += digitalTwinController.Shutdown;
            Console.ReadLine();
        }
    }

    public class MicrosoftFlightSimulatorConnection
    {
        public int PollInterval;

        /// <summary>
        /// Constructs MSFS object with polling interval param
        /// </summary>
        /// <param name="interval"></param>
        public MicrosoftFlightSimulatorConnection(int interval)
        {
            PollInterval = interval;
        }

        public Aircraft Aircraft;

        public SIMCONNECT_SIMOBJECT_TYPE SimObjectType = SIMCONNECT_SIMOBJECT_TYPE.USER;
        public List<SimvarRequest> SimvarRequests = new List<SimvarRequest>();

        private uint CurrentDefinition = 0;
        private uint CurrentRequest = 0;

        public Action<ITwinable> OnUserAircraftCreated;
        public Action<ITwinable, string> OnSimulationObjectReceived;
        public Action<ITwinable> OnSimulationExit;

        // String properties must be packed inside of a struct
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        struct Struct1
        {
            // this is how you declare a fixed size string
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public String sValue;

            // other definitions can be added to this struct
            // ...
        };

        /// SimConnect object
        private SimConnect simConnect = null;
        private Thread simConnectThread = null;
        private bool mQuit = false;

        private enum EventIdentifier
        {
            MissionCompleted = 0,
        }

        private enum MissionStatus
        {
            MissionStatusFailed,
            MissionStatusCrashed,
            MissionStatusSucceeded,
        };

        public bool Connect()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Attempting a connection to MSFS2020...");

            bool result = false;

            try
            {
                simConnectThread = new Thread(new ThreadStart(StartSimConnectThread));
                simConnectThread.IsBackground = true;
                simConnectThread.Start();
                result = true;
            }
            catch
            {
                Console.WriteLine("ERROR: Failed to create and start thread");
            }

            return result;
        }

        private void StartSimConnectThread()
        {
            Thread.Sleep(2500);

            if (PollForConnection())
            {
                if (simConnect != null)
                {
                    while (!mQuit)
                    {
                        try
                        {
                            simConnect.ReceiveMessage();
                        }
                        catch
                        {
                            Console.WriteLine("ERROR: Failed to recive a message from simconnect");
                        }

                        Thread.Sleep(500);
                    }

                    if (mQuit)
                    {
                        simConnect.Dispose();
                        simConnect = null;
                    }
                }
            }
            else
            {
                Console.WriteLine("ERROR: SimConnect failed to connect (timed out).");
            }
        }

        private bool PollForConnection()
        {
            int retryCounter = 1000;

            while (simConnect == null && retryCounter > 0)
            {
                try
                {
                    simConnect = new SimConnect("Managed Scenario Controller", IntPtr.Zero, 0, null, 0);
                    if (simConnect != null)
                    {
                        simConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(SimConnect_OnRecvOpen);
                        simConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(SimConnect_OnRecvQuit);
                        simConnect.OnRecvEvent += new SimConnect.RecvEventEventHandler(SimConnect_OnRecvEvent);
                        simConnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(SimConnect_OnRecvException);
                        simConnect.OnRecvSimobjectDataBytype += new SimConnect.RecvSimobjectDataBytypeEventHandler(SimConnect_OnRecvSimobjectDataBytype);
                        simConnect.SubscribeToSystemEvent(EventIdentifier.MissionCompleted, "MissionCompleted");
                        simConnect.SetSystemEventState(EventIdentifier.MissionCompleted, SIMCONNECT_STATE.ON);
                    }
                }
                catch
                {
                    Console.WriteLine("ERROR: Failed to poll for connection");
                }

                if (simConnect != null)
                {
                    Thread.Sleep(500);
                    --retryCounter;
                }
            }

            return (retryCounter > 0);
        }

        private void AddRequest(string simvarRequest, string newUnitRequest, bool isString, Aircraft aircraft)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Adding Request {simvarRequest}");

            SimvarRequest oSimvarRequest = new SimvarRequest
            {
                Def = (DEFINITION)CurrentDefinition,
                Request = (REQUEST)CurrentRequest,
                Name = simvarRequest,
                IsString = isString,
                Units = isString ? null : newUnitRequest,
                Twin = aircraft
            };

            oSimvarRequest.Pending = !RegisterToSimConnect(oSimvarRequest);

            SimvarRequests.Add(oSimvarRequest);

            aircraft.aircraftTelemetryValues.Add(oSimvarRequest);

            ++CurrentDefinition;
            ++CurrentRequest;
        }

        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Connection to MSFS2020 Successful");

            Aircraft = new Aircraft("F151", "dtmi:com:adt:Aircraft;1");

            AddRequest("PLANE PITCH DEGREES", "radians", false, Aircraft);
            AddRequest("PLANE ALTITUDE", "feet", false, Aircraft);
            AddRequest("PLANE HEADING DEGREES TRUE", "radians", false, Aircraft);
            AddRequest("PLANE LONGITUDE", "radians", false, Aircraft);
            AddRequest("PLANE LATITUDE", "radians", false, Aircraft);
            AddRequest("AIRSPEED INDICATED", "knots", false, Aircraft);

            OnUserAircraftCreated?.Invoke(Aircraft);

            PollTelemetryRequests();
        }

        public void PollTelemetryRequests()
        {
            while (simConnect != null)
            {
                try
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Polling for Telemetry Requests from MSFS...");

                    foreach (SimvarRequest simvarRequest in SimvarRequests)
                    {
                        simConnect.ReceiveMessage();
                        simConnect?.RequestDataOnSimObjectType(simvarRequest.Request, simvarRequest.Def, 0, SimObjectType);
                    }
                }
                catch
                {
                    Console.WriteLine("ERROR: Failed to poll for connection");
                }

                if (simConnect != null)
                {
                    Thread.Sleep(PollInterval);
                }

                Console.Clear();
            }
        }

        private bool RegisterToSimConnect(SimvarRequest simvarRequest)
        {
            if (simConnect != null)
            {
                if (simvarRequest.IsString)
                {
                    /// Define a data structure containing string value
                    simConnect.AddToDataDefinition(simvarRequest.Def, simvarRequest.Name, "", SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                    /// IMPORTANT: Register it with the simconnect managed wrapper marshaller
                    /// If you skip this step, you will only receive a uint in the .dwData field.
                    simConnect.RegisterDataDefineStruct<Struct1>(simvarRequest.Def);
                }
                else
                {
                    /// Define a data structure containing numerical value
                    simConnect.AddToDataDefinition(simvarRequest.Def, simvarRequest.Name, simvarRequest.Units, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                    /// IMPORTANT: Register it with the simconnect managed wrapper marshaller
                    /// If you skip this step, you will only receive a uint in the .dwData field.
                    simConnect.RegisterDataDefineStruct<double>(simvarRequest.Def);
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        private void SimConnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            switch ((EventIdentifier)data.uEventID)
            {
                case EventIdentifier.MissionCompleted:
                {
                    switch ((MissionStatus)data.dwData)
                    {
                        case MissionStatus.MissionStatusFailed:
                            {
                                System.Console.WriteLine("Mission status: Failed.");
                                break;
                            }
                        case MissionStatus.MissionStatusCrashed:
                            {
                                System.Console.WriteLine("Mission status: Crashed.");
                                break;
                            }
                        case MissionStatus.MissionStatusSucceeded:
                            {
                                System.Console.WriteLine("Mission status: Succeded.");
                                break;
                            }
                    }

                    break;
                }
            }
        }

        private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Console.WriteLine("SimConnect_OnRecvQuit");

            OnSimulationExit?.Invoke(Aircraft);

            if (simConnect != null)
            {
                simConnect.Dispose();
                simConnect = null;
            }
        }

        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("SimConnect Exception");
        }

        private void SimConnect_OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.ResetColor();

            foreach (SimvarRequest oSimvarRequest in SimvarRequests)
            {
                if (data.dwRequestID == (uint)oSimvarRequest.Request)
                {
                    if (oSimvarRequest.IsString)
                    {
                        Struct1 result = (Struct1)data.dwData[0];
                        oSimvarRequest.Value = 0;
                        oSimvarRequest.sValue = result.sValue;

                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"{oSimvarRequest.Name}: {oSimvarRequest.sValue}", Console.ForegroundColor);
                        Console.ResetColor();
                    }
                    else
                    {
                        double dValue = (double)data.dwData[0];
                        oSimvarRequest.Value = dValue;
                        oSimvarRequest.sValue = dValue.ToString("F9");

                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"{oSimvarRequest.Name}: {oSimvarRequest.sValue}", Console.ForegroundColor);
                        Console.ResetColor();
                    }
                }
            }

            Dictionary<string, Object> telemetryPayload = new Dictionary<string, Object>();

            telemetryPayload.Add("AIRCRAFT_ID", Aircraft.TwinId);

            foreach (var request in Aircraft.aircraftTelemetryValues)
            {
                telemetryPayload.Add(request.Name.Replace(" ", "_"), request.Value);
            }

            OnSimulationObjectReceived(Aircraft, JsonSerializer.Serialize(telemetryPayload));
        }
    }

    public class DigitalTwinIngressControlller
    {
        private readonly HttpClient httpClient = new HttpClient();
        public DigitalTwinsClient Client;
        public Dictionary<string, BasicDigitalTwin> TwinInstances = new Dictionary<string, BasicDigitalTwin>();

        public DigitalTwinIngressControlller(string adtUrl)
        {
            Console.WriteLine("Creating ADT Data Ingress Client...");
            DefaultAzureCredential credentials = new DefaultAzureCredential();
            Client = new DigitalTwinsClient(new Uri(adtUrl), credentials, new DigitalTwinsClientOptions { Transport = new HttpClientTransport(httpClient) });
            Console.WriteLine($"SUCCESS: Created ADT Data Ingress Client at {DateTime.Now}");
        }

        public async void Standup(ITwinable twin)
        {
            if (TwinInstances.ContainsValue(twin.DigitalTwin))
            {
                Console.WriteLine("ADT already exsits during this runtime and does not not initialization");
                return;
            }

            Response<BasicDigitalTwin> response = await CreateOrReplaceDigitalTwinInstance(twin);
            TwinInstances.Add(twin.TwinId, response.Value);
        }

        public async void Shutdown(ITwinable twin)
        {
            if (TwinInstances.ContainsValue(twin.DigitalTwin))
            {
                await DeleteTwinInstance(twin);
            }
        }

        public async Task<Response<BasicDigitalTwin>> GetDigitalTwinAsync(ITwinable twin)
        {
            Console.WriteLine($"Getting digital twin {twin.TwinId}");
            try
            {
                Response<BasicDigitalTwin> response = await Client.GetDigitalTwinAsync<BasicDigitalTwin>(twin.TwinId);
                Console.WriteLine($"SUCCESS: ADT instance {twin.TwinId} retrieved");
                return response;
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to get digital twin instance due to:\n{ex}");
            }
        }

        public async Task<Response<BasicDigitalTwin>> CreateOrReplaceDigitalTwinInstance(ITwinable twin)
        {
            Console.WriteLine($"Creating digital twin of type {twin.DigitalTwin.Metadata.ModelId} for MSFS instance {twin.TwinId}");
            try
            {
                Response<BasicDigitalTwin> response = await Client.CreateOrReplaceDigitalTwinAsync<BasicDigitalTwin>(twin.TwinId, twin.DigitalTwin);
                Console.WriteLine($"SUCCESS: ADT instance created or replaced for twin instance {twin.TwinId}");
                return response;
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to create digital twin instance due to:\n{ex}");
            }
        }

        public async void PublishTelemetry(ITwinable twin, string payload)
        {
            try
            {
                Response reponse = await Client.PublishTelemetryAsync(twin.TwinId, Guid.NewGuid().ToString(), payload);
                Console.WriteLine($"SUCCESS {reponse.ClientRequestId}: Published digital twin telemetry for ADT instance {twin.TwinId}. Payload Message: {payload}");
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to publish digital twin telemetry due to:\n{ex}");
            }
        }

        public async Task<Response> DeleteTwinInstance(ITwinable twin)
        {
            Console.WriteLine($"Deleting digital twin {twin.TwinId}");
            try
            {
                Response response = await Client.DeleteDigitalTwinAsync(twin.TwinId);
                await Client.DeleteDigitalTwinAsync(twin.TwinId);
                Console.WriteLine($"SUCCESS: ADT instance {twin.TwinId} deleted from resource group");
                return response;
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to delete digital twin instance due to:\n{ex}");
            }
        }
    }
}
