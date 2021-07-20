using Azure;
using Azure.Core.Pipeline;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.FlightSimulator.SimConnect;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace IngressClientADT
{
    public class LongitudeTelemetry
    {
        public double PLANE_LONGITUDE { get; set; }
    }

    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();

        private static string adtServiceUrl = "https://immersiveadt.api.eus.digitaltwins.azure.net";

        static void Main(string[] args)
        {
            var credentials = new DefaultAzureCredential();

            DigitalTwinsClient client = new DigitalTwinsClient(new Uri(adtServiceUrl), credentials, new DigitalTwinsClientOptions { Transport = new HttpClientTransport(httpClient) });

            SendTelemetry(client);

            Console.ReadLine();
        }

        public static async void SendTelemetry(DigitalTwinsClient client)
        {
            AsyncPageable<DigitalTwinsModelData> modelDataList = client.GetModelsAsync();

            // Used for the ID of the twin instance
            int i = 0;

            await foreach (DigitalTwinsModelData md in modelDataList)
            {
                Console.WriteLine($"Model: {md.Id}");
                LongitudeTelemetry longitudeTelemetry = new LongitudeTelemetry { PLANE_LONGITUDE = -85.576385 };

                // Create the twin instance
                var f15 = new BasicDigitalTwin();
                f15.Metadata.ModelId = md.Id;

                try
                {
                    f15.Id = $"F15{i}";
                    await client.CreateOrReplaceDigitalTwinAsync<BasicDigitalTwin>(f15.Id, f15);
                    Console.WriteLine($"Created twin: {f15.Id}");

                    // Publish the digital twin telemetry to azure intsance
                    await client.PublishTelemetryAsync(f15.Id, Guid.NewGuid().ToString(), JsonConvert.SerializeObject(longitudeTelemetry));
                }
                catch (RequestFailedException e)
                {
                    Console.WriteLine($"Create twin error: {e.Status}: {e.Message}");
                }

                i++;
            }

            /*JObject deviceMessage = (JObject)JsonConvert.DeserializeObject(*//*Convert MSFS data into device message json for digital twin ingress*//*);

            string deviceId = (string)deviceMessage["systemProperties"]["iothub-connection-device-id"];
            var ID = deviceMessage["body"]["TurbineID"];
            var TimeInterval = deviceMessage["body"]["TimeInterval"];
            var Description = deviceMessage["body"]["Description"];
            var Code = deviceMessage["body"]["Code"];
            var WindSpeed = deviceMessage["body"]["WindSpeed"];
            var Ambient = deviceMessage["body"]["Ambient"];
            var Rotor = deviceMessage["body"]["Rotor"];
            var Power = deviceMessage["body"]["Power"];

            var updateProperty = new JsonPatchDocument();
            var turbineTelemetry = new Dictionary<string, Object>()
            {
                ["TurbineID"] = ID,
                ["TimeInterval"] = TimeInterval,
                ["Description"] = Description,
                ["Code"] = Code,
                ["WindSpeed"] = WindSpeed,
                ["Ambient"] = Ambient,
                ["Rotor"] = Rotor,
                ["Power"] = Power
            };
            updateProperty.AppendAdd("/TurbineID", ID.Value<string>());*/

        }
    }
}
