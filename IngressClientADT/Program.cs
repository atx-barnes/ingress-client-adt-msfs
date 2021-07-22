using Azure;
using Azure.Core.Pipeline;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.FlightSimulator.SimConnect;
using System.Windows.Forms;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace IngressClientADT
{
    class Program
    {
        static void Main(string[] args)
        {
            //MicrosoftFlightSimulatorDigitalTwinIngressControlller digitalTwinController = new MicrosoftFlightSimulatorDigitalTwinIngressControlller("https://immersiveadt.api.eus.digitaltwins.azure.net");
            Console.WriteLine("Press enter to connect to MSFS");
            Console.ReadLine();

            MicrosoftFlightSimulatorConnection microsoftFlightSimulatorConnection = new MicrosoftFlightSimulatorConnection();
            microsoftFlightSimulatorConnection.Start();
            Console.ReadLine();
        }
    }

    public class LongitudeTelemetry
    {
        public double PLANE_LONGITUDE { get; set; }
    }

    // https://www.fsdeveloper.com/forum/threads/c-console-app.451567/
    // https://www.prepar3d.com/SDKv4/sdk/simconnect_api/samples/managed_scenario_controller.html
    public class MicrosoftFlightSimulatorConnection
    {
        public enum DEFINITION
        {
            Dummy = 0
        };

        public enum REQUEST
        {
            Dummy = 0,
            Struct1
        };

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

        public class SimvarRequest
        {
            public DEFINITION eDef = DEFINITION.Dummy;
            public REQUEST eRequest = REQUEST.Dummy;
            public string sName { get; set; }
            public bool bIsString { get; set; }
            public double dValue = 0.0;
            public string sValue = null;
            public string sUnits { get; set; }
        };

        /// SimConnect object
        private SimConnect simConnect = null;
        private SimConnect SimConnect = null;
        private Thread SimConnectThread = null;
        private bool mQuit = false;

        public enum EventIdentifier
        {
            MissionCompleted = 0,
        }

        enum MissionStatus
        {
            MissionStatusFailed,
            MissionStatusCrashed,
            MissionStatusSucceeded,
        };

        public bool Start()
        {
            bool result = false;

            try
            {
                SimConnectThread = new Thread(new ThreadStart(StartSimConnectThread));
                SimConnectThread.IsBackground = true;
                SimConnectThread.Start();
                result = true;
            }
            catch
            {
                Console.WriteLine("ERROR: Failed to create and start thread");
            }

            return result;
        }

        private bool PollForConnection()
        {
            int retryCounter = 1000;

            while (SimConnect == null && retryCounter > 0)
            {
                try
                {
                    SimConnect = new SimConnect("Managed Scenario Controller", IntPtr.Zero, 0, null, 0);
                    if (SimConnect != null)
                    {
                        SimConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(SimConnect_OnRecvOpen);
                        SimConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(SimConnect_OnRecvQuit);
                        SimConnect.OnRecvEvent += new SimConnect.RecvEventEventHandler(SimConnect_OnRecvEvent);
                        SimConnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(SimConnect_OnRecvException);
                        SimConnect.OnRecvSimobjectDataBytype += new SimConnect.RecvSimobjectDataBytypeEventHandler(SimConnect_OnRecvSimobjectDataBytype);
                        SimConnect.SubscribeToSystemEvent(EventIdentifier.MissionCompleted, "MissionCompleted");
                        SimConnect.SetSystemEventState(EventIdentifier.MissionCompleted, SIMCONNECT_STATE.ON);
                    }
                }
                catch
                {
                    Console.WriteLine("ERROR: Failed to poll for connection");
                }

                if (SimConnect == null)
                {
                    Thread.Sleep(500);
                    --retryCounter;
                }
            }

            return (retryCounter > 0);
        }

        private void StartSimConnectThread()
        {
            Thread.Sleep(2500);

            if (PollForConnection())
            {
                if (SimConnect != null)
                {
                    while (!mQuit)
                    {
                        try
                        {
                            SimConnect.ReceiveMessage();
                        }
                        catch
                        {
                        }

                        Thread.Sleep(500);
                    }

                    if (mQuit)
                    {
                        SimConnect.Dispose();
                        SimConnect = null;
                    }
                }
            }
            else
            {
                System.Console.WriteLine("ERROR: SimConnect failed to connect (timed out).");
            }
        }

        void SimConnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            switch ((EventIdentifier)data.uEventID)
            {
                // Mission completion result.
                // This type of scenario information could potentially be used by an LMS,
                // however is beyond the scope of this sample.
                case EventIdentifier.MissionCompleted:
                    {
                        switch ((MissionStatus)data.dwData)
                        {
                            case MissionStatus.MissionStatusFailed:
                                {
                                    System.Console.WriteLine("Mission completion status: FAILED.");
                                    break;
                                }
                            case MissionStatus.MissionStatusCrashed:
                                {
                                    System.Console.WriteLine("Mission completion status: CRASHED.");
                                    break;
                                }
                            case MissionStatus.MissionStatusSucceeded:
                                {
                                    System.Console.WriteLine("Mission completion status: SUCCEEDED.");
                                    break;
                                }
                        }

                        break;
                    }
            }
        }

        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Console.WriteLine("SimConnect_OnRecvOpen");
        }

        /// The case where the user closes game
        private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Console.WriteLine("SimConnect_OnRecvQuit");

            if (simConnect != null)
            {
                simConnect.Dispose();
                simConnect = null;
            }
        }

        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Console.WriteLine("SimConnect Exception");
        }

        private void SimConnect_OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            Console.WriteLine("SimConnect_OnRecvSimobjectDataBytype");
        }

        private bool RegisterToSimConnect(SimvarRequest simvarRequest)
        {
            if (simConnect != null)
            {
                if (simvarRequest.bIsString)
                {
                    /// Define a data structure containing string value
                    simConnect.AddToDataDefinition(simvarRequest.eDef, simvarRequest.sName, "", SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    /// IMPORTANT: Register it with the simconnect managed wrapper marshaller
                    /// If you skip this step, you will only receive a uint in the .dwData field.
                    simConnect.RegisterDataDefineStruct<Struct1>(simvarRequest.eDef);
                }
                else
                {
                    /// Define a data structure containing numerical value
                    simConnect.AddToDataDefinition(simvarRequest.eDef, simvarRequest.sName, simvarRequest.sUnits, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                    /// IMPORTANT: Register it with the simconnect managed wrapper marshaller
                    /// If you skip this step, you will only receive a uint in the .dwData field.
                    simConnect.RegisterDataDefineStruct<double>(simvarRequest.eDef);
                }

                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public class MicrosoftFlightSimulatorDigitalTwinIngressControlller
    {
        private readonly HttpClient httpClient = new HttpClient();
        private DigitalTwinsClient client;
        private string adtServiceUrl;
        public Dictionary<string, DigitalTwinsModelData> adtModels = new Dictionary<string, DigitalTwinsModelData>();

        public MicrosoftFlightSimulatorDigitalTwinIngressControlller(string adtUrl)
        {
            adtServiceUrl = adtUrl;

            CreateADTClientInstance();
        }

        private void CreateADTClientInstance()
        {
            DefaultAzureCredential credentials = new DefaultAzureCredential();
            client = new DigitalTwinsClient(new Uri(adtServiceUrl), credentials, new DigitalTwinsClientOptions { Transport = new HttpClientTransport(httpClient) });

            // Retrieve all models in ADT instance when client is created
            GetDigitalTwinModelsAsync();
        }

        private async void GetDigitalTwinModelsAsync()
        {
            try
            {
                AsyncPageable<DigitalTwinsModelData> allModels = client.GetModelsAsync();

                await foreach (DigitalTwinsModelData model in allModels)
                {
                    adtModels.Add(model.Id, model);
                }
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Failed to get all the models due to:\n{ex}");
            }
        }

        public async Task<Response<BasicDigitalTwin>> CreateTwinInstanceFromModel(ITwinable twinableObject)
        {
            Console.WriteLine($"Creating digital twin of type {twinableObject.DigitalTwin.Metadata.ModelId} for MSFS instance {twinableObject.InstanceID}");

            try
            {
                return await client.CreateOrReplaceDigitalTwinAsync<BasicDigitalTwin>(twinableObject.InstanceID, twinableObject.DigitalTwin);
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to create digital twin instance due to:\n{ex}");
            }
        }

        public async Task<Response> DeleteTwinInstance(string dtid)
        {
            try
            {
                return await client.DeleteDigitalTwinAsync(dtid);
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to delete digital twin instance due to:\n{ex}");
            }
        }

        public async Task<Response> SendTelemetryData<T>(string dtid, T data)
        {
            return await client.PublishTelemetryAsync(dtid, Guid.NewGuid().ToString(), JsonConvert.SerializeObject(data));
        }
    }
}
