using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.DigitalTwins.Core;

namespace IngressClientADT
{
    public class Aircraft : ITwinable
    {
        public BasicDigitalTwin DigitalTwin { get; set; }

        public string TwinId { get; private set ; }

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

