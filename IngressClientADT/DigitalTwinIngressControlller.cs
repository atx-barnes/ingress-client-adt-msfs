using Azure;
using Azure.Core.Pipeline;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using System;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace IngressClientADT
{
    public class DigitalTwinIngressControlller
    {
        private readonly DigitalTwinsClient adtClient;
        private readonly HttpClient httpClient = new HttpClient();
        private readonly Dictionary<string, BasicDigitalTwin> twins = new Dictionary<string, BasicDigitalTwin>();

        public DigitalTwinIngressControlller(string adtUrl)
        {
            Console.WriteLine("Creating ADT Data Ingress Client...");
            DefaultAzureCredential credentials = new DefaultAzureCredential();
            adtClient = new DigitalTwinsClient(new Uri(adtUrl), credentials, new DigitalTwinsClientOptions { Transport = new HttpClientTransport(httpClient) });
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"SUCCESS: Created ADT Data Ingress Controlller at {DateTime.Now}");
            Console.ResetColor();
        }

        public async void StandupHandle(ITwinable twin)
        {
            if (twins.ContainsKey(twin.TwinId))
            {
                Console.WriteLine("ADT already exsits during this runtime and does not not initialization");
                return;
            }

            Response<BasicDigitalTwin> response = await CreateOrReplaceDigitalTwinInstance(twin);
            twins.Add(twin.TwinId, response.Value);
        }

        public async void ShutdownHandle(ITwinable twin)
        {
            if (twins.ContainsKey(twin.TwinId))
            {
                await DeleteTwinInstance(twin);
            }
        }

        public async void PublishTelemetryHandle(ITwinable twin, string payload)
        {
            if (twins.ContainsKey(twin.TwinId))
            {
                await PublishTelemetry(twin, payload);
            }
        }

        private async Task<Response<BasicDigitalTwin>> GetDigitalTwinAsync(ITwinable twin)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Getting digital twin {twin.TwinId}");
            Console.ResetColor();
            try
            {
                Response<BasicDigitalTwin> response = await adtClient.GetDigitalTwinAsync<BasicDigitalTwin>(twin.TwinId);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"SUCCESS: ADT instance {twin.TwinId} retrieved");
                Console.ResetColor();
                return response;
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to get digital twin instance due to:\n{ex}");
            }
        }

        private async Task<Response<BasicDigitalTwin>> CreateOrReplaceDigitalTwinInstance(ITwinable twin)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Creating digital twin of type {twin.DigitalTwin.Metadata.ModelId} for MSFS instance {twin.TwinId}");
            Console.ResetColor();
            try
            {
                Response<BasicDigitalTwin> response = await adtClient.CreateOrReplaceDigitalTwinAsync<BasicDigitalTwin>(twin.TwinId, twin.DigitalTwin);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"SUCCESS: ADT instance created or replaced for twin instance {twin.TwinId}");
                Console.ResetColor();
                return response;
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to create digital twin instance due to:\n{ex}");
            }
        }

        private async Task<Response> PublishTelemetry(ITwinable twin, string payload)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Publishing telemetry events digital twin for MSFS instance {twin.TwinId} with payload {payload}");
            Console.ResetColor();
            try
            {
                Response reponse = await adtClient.PublishTelemetryAsync(twin.TwinId, Guid.NewGuid().ToString(), payload);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"SUCCESS {reponse.ClientRequestId}: Published digital twin telemetry for ADT instance {twin.TwinId}. Payload Message: {payload}");
                Console.ResetColor();
                return reponse;
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to publish digital twin telemetry due to:\n{ex}");
            }
        }

        private async Task<Response> DeleteTwinInstance(ITwinable twin)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"Deleting digital twin {twin.TwinId}");
            Console.ResetColor();
            try
            {
                Response response = await adtClient.DeleteDigitalTwinAsync(twin.TwinId);
                await adtClient.DeleteDigitalTwinAsync(twin.TwinId);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"SUCCESS: ADT instance {twin.TwinId} deleted from resource group");
                Console.ResetColor();
                return response;
            }
            catch (RequestFailedException ex)
            {
                throw new Exception($"Failed to delete digital twin instance due to:\n{ex}");
            }
        }
    }
}
