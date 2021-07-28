using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Text.Json;
using System.Threading;
using System.Collections.Generic;

namespace IngressClientADT
{
    public class MicrosoftFlightSimulatorConnection
    {
        public Aircraft Aircraft { get; private set; }
        public Action<ITwinable> OnUserAircraftCreated { get; set; }
        public Action<ITwinable, string> OnSimulationObjectReceived { get; set; }
        public Action<ITwinable> OnSimulationExit { get; set; }
        private int SimulationDataRequestInterval { get; set; }
        private uint CurrentDefinition { get; set; }
        private uint CurrentRequest { get; set; }
        private SimConnect SimConnect { get; set; }
        private Thread SimConnectThread { get; set; }
        private bool Quit { get; set; }

        private readonly List<SimvarRequest> simvarRequests = new List<SimvarRequest>();

        public enum DEFINITION { Dummy = 0 };
        public enum REQUEST { Dummy = 0, Struct1 };

        public class SimvarRequest
        {
            public DEFINITION Def = DEFINITION.Dummy;
            public REQUEST Request = REQUEST.Dummy;
            public string Name { get; set; }
            public string DTDLName { get; set; }
            public bool IsString { get; set; }
            public double Value = 0.0;
            public string sValue = null;
            public string Units { get; set; }
            public ITwinable Twin { get; set; }
            public bool Pending = true;
        };

        private sealed class RequestType
        {
            public static readonly RequestType Pitch = new RequestType("PLANE PITCH DEGREES");
            public static readonly RequestType Altitude = new RequestType("PLANE ALTITUDE");
            public static readonly RequestType Heading = new RequestType("PLANE HEADING DEGREES TRUE");
            public static readonly RequestType Longitude = new RequestType("PLANE LONGITUDE");
            public static readonly RequestType Latitude = new RequestType("PLANE LATITUDE");
            public static readonly RequestType Airspeed = new RequestType("AIRSPEED INDICATED");
            public static readonly RequestType Bank = new RequestType("PLANE BANK DEGREES");

            private RequestType(string value)
            {
                Value = value;
            }

            public string Value { get; private set; }
        }

        private sealed class RequestUnit
        {
            public static readonly RequestUnit Radians = new RequestUnit("radians");
            public static readonly RequestUnit Feet = new RequestUnit("feet");
            public static readonly RequestUnit Knots = new RequestUnit("knots");

            private RequestUnit(string unit)
            {
                Value = unit;
            }

            public string Value { get; private set; }
        }

        private enum EventIdentifier { MissionCompleted = 0, }

        private enum MissionStatus
        {
            MissionStatusFailed,
            MissionStatusCrashed,
            MissionStatusSucceeded,
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

        /// <summary>
        /// Constructs MSFS object with polling interval param
        /// </summary>
        /// <param name="interval"></param>
        public MicrosoftFlightSimulatorConnection(int interval)
        {
            SimulationDataRequestInterval = interval;
        }

        public bool Connect()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Attempting a connection to MSFS2020...");
            Console.ResetColor();

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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: Failed to create and start thread");
                Console.ResetColor();
            }

            return result;
        }

        private void StartSimConnectThread()
        {
            Thread.Sleep(2500);

            if (PollForConnection())
            {
                if (SimConnect != null)
                {
                    while (!Quit)
                    {
                        try
                        {
                            SimConnect.ReceiveMessage();
                        }
                        catch
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("ERROR: Failed to recive a message from simconnect");
                            Console.ResetColor();
                        }

                        Thread.Sleep(500);
                    }

                    if (Quit)
                    {
                        SimConnect.Dispose();
                        SimConnect = null;
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: SimConnect failed to connect (timed out).");
                Console.ResetColor();
            }
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
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR: Failed to poll for connection");
                    Console.ResetColor();
                }

                if (SimConnect != null)
                {
                    Thread.Sleep(500);
                    --retryCounter;
                }
            }

            return (retryCounter > 0);
        }

        private void AddRequest(string simvarRequest, string newUnitRequest, bool isString, Aircraft aircraft, string dtdlName)
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
                Twin = aircraft,
                DTDLName = dtdlName
            };

            oSimvarRequest.Pending = !RegisterToSimConnect(oSimvarRequest);

            simvarRequests.Add(oSimvarRequest);

            aircraft.AircraftTelemetryValues.Add(oSimvarRequest);

            ++CurrentDefinition;
            ++CurrentRequest;
        }

        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Connection to MSFS2020 Successful");
            Console.ResetColor();

            Aircraft = new Aircraft("F151", "dtmi:com:adt:Aircraft;1");

            AddRequest(RequestType.Pitch.Value, RequestUnit.Radians.Value, false, Aircraft, "Pitch");
            AddRequest(RequestType.Altitude.Value, RequestUnit.Feet.Value, false, Aircraft, "Altitude");
            AddRequest(RequestType.Heading.Value, RequestUnit.Radians.Value, false, Aircraft, "Heading");
            AddRequest(RequestType.Longitude.Value, RequestUnit.Radians.Value, false, Aircraft, "Longitude");
            AddRequest(RequestType.Latitude.Value, RequestUnit.Radians.Value, false, Aircraft, "Latitude");
            AddRequest(RequestType.Airspeed.Value, RequestUnit.Knots.Value, false, Aircraft, "Airspeed");
            AddRequest(RequestType.Bank.Value, RequestUnit.Radians.Value, false, Aircraft, "Bank");

            OnUserAircraftCreated?.Invoke(Aircraft);

            PollTelemetryRequests();
        }

        private void PollTelemetryRequests()
        {
            while (SimConnect != null)
            {
                try
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Polling for Telemetry Requests from MSFS...");
                    Console.ResetColor();

                    foreach (SimvarRequest simvarRequest in simvarRequests)
                    {
                        SimConnect.ReceiveMessage();
                        SimConnect?.RequestDataOnSimObjectType(simvarRequest.Request, simvarRequest.Def, 0, SIMCONNECT_SIMOBJECT_TYPE.USER);
                    }
                }
                catch
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("ERROR: Failed to poll for connection");
                    Console.ResetColor();
                }

                if (SimConnect != null)
                {
                    Thread.Sleep(SimulationDataRequestInterval);
                }

                Console.Clear();
            }
        }

        private bool RegisterToSimConnect(SimvarRequest simvarRequest)
        {
            if (SimConnect != null)
            {
                if (simvarRequest.IsString)
                {
                    /// Define a data structure containing string value
                    SimConnect.AddToDataDefinition(simvarRequest.Def, simvarRequest.Name, "", SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                    /// IMPORTANT: Register it with the simconnect managed wrapper marshaller
                    /// If you skip this step, you will only receive a uint in the .dwData field.
                    SimConnect.RegisterDataDefineStruct<Struct1>(simvarRequest.Def);
                }
                else
                {
                    /// Define a data structure containing numerical value
                    SimConnect.AddToDataDefinition(simvarRequest.Def, simvarRequest.Name, simvarRequest.Units, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                    /// IMPORTANT: Register it with the simconnect managed wrapper marshaller
                    /// If you skip this step, you will only receive a uint in the .dwData field.
                    SimConnect.RegisterDataDefineStruct<double>(simvarRequest.Def);
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
                                    Console.WriteLine("Mission status: Failed.");
                                    break;
                                }
                            case MissionStatus.MissionStatusCrashed:
                                {
                                    Console.WriteLine("Mission status: Crashed.");
                                    break;
                                }
                            case MissionStatus.MissionStatusSucceeded:
                                {
                                    Console.WriteLine("Mission status: Succeded.");
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

            if (SimConnect != null)
            {
                SimConnect.Dispose();
                SimConnect = null;
            }
        }

        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("SimConnect Exception");
            Console.ResetColor();
        }

        private void SimConnect_OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.ResetColor();

            foreach (SimvarRequest oSimvarRequest in simvarRequests)
            {
                if (data.dwRequestID == (uint)oSimvarRequest.Request)
                {
                    if (oSimvarRequest.IsString)
                    {
                        Struct1 result = (Struct1)data.dwData[0];
                        oSimvarRequest.Value = 0;
                        oSimvarRequest.sValue = result.sValue;

                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"SimConnect SimvarRequest {oSimvarRequest.Name} - Received with Value: {oSimvarRequest.sValue}", Console.ForegroundColor);
                        Console.ResetColor();
                    }
                    else
                    {
                        double dValue = (double)data.dwData[0];
                        oSimvarRequest.Value = dValue;
                        oSimvarRequest.sValue = dValue.ToString("F9");

                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine($"SimConnect SimvarRequest {oSimvarRequest.Name} - Received with Value: {oSimvarRequest.sValue}", Console.ForegroundColor);
                        Console.ResetColor();
                    }
                }
            }

            Dictionary<string, Object> telemetryPayload = new Dictionary<string, Object>();
            telemetryPayload.Add("Id", Aircraft.TwinId);
            foreach (var request in Aircraft.AircraftTelemetryValues) { telemetryPayload.Add(request.DTDLName, request.Value); }
            OnSimulationObjectReceived(Aircraft, JsonSerializer.Serialize(telemetryPayload));
        }
    }
}
