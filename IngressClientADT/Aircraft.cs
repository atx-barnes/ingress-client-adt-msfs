using System;
using System.Collections.Generic;
using Azure.DigitalTwins.Core;

namespace IngressClientADT
{
    public class Aircraft : ITwinable
    {
        public BasicDigitalTwin DigitalTwin { get; private set; }

        public string TwinId { get; private set; }

        public List<MicrosoftFlightSimulatorConnection.SimvarRequest> AircraftTelemetryValues = new List<MicrosoftFlightSimulatorConnection.SimvarRequest>();

        public Aircraft(string instanceId, string modelId)
        {
            TwinId = instanceId;

            DigitalTwin = new BasicDigitalTwin
            {
                Id = TwinId,
                Metadata = { ModelId = modelId }
            };
        }
    }
}

