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

        public string InstanceID { get; private set ; }

        public Aircraft(string instanceId, string modelId, Dictionary<string, string> contents)
        {
            InstanceID = instanceId;

            DigitalTwin = new BasicDigitalTwin
            {
                Id = InstanceID,
                Metadata = { ModelId = modelId }
            };

            foreach (string key in contents.Keys)
            {
                DigitalTwin.Contents.Add(key, contents[key]);
            }
        }
    }
}

