using System;

namespace IngressClientADT
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Welcome to the Azure Digital Twin Data Ingress Client for Microsoft Flight Simulator 2020");
            DigitalTwinIngressControlller digitalTwinController = new DigitalTwinIngressControlller("https://immersiveadtadthizi25q7e2.api.eus.digitaltwins.azure.net");
            Console.WriteLine("Press Enter to Connect to MSFS2020...");
            Console.ReadLine();

            MicrosoftFlightSimulatorConnection microsoftFlightSimulatorConnection = new MicrosoftFlightSimulatorConnection(500);

            microsoftFlightSimulatorConnection.Connect();
            microsoftFlightSimulatorConnection.OnUserAircraftCreated += digitalTwinController.StandupHandle;
            microsoftFlightSimulatorConnection.OnSimulationObjectReceived += digitalTwinController.PublishTelemetryHandle;
            microsoftFlightSimulatorConnection.OnSimulationExit += digitalTwinController.ShutdownHandle;
            Console.ReadLine();
        }
    }
}
