// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using IoTProcessorManagement.Clients;
    using Microsoft.ServiceBus.Messaging;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    internal static class InternalFunctions
    {
        public static async Task<string> GetManagementApiEndPointAsync(string FabricEndPoint, string sMgmtAppInstanceName)
        {
            FabricClient fc = new FabricClient(FabricEndPoint);
            ResolvedServicePartition partition = await fc.ServiceManager.ResolveServicePartitionAsync(new Uri(sMgmtAppInstanceName));

            JObject jsonAddress = JObject.Parse(partition.GetEndpoint().Address);
            string address = (string) jsonAddress["Endpoints"][""];

            return address;
        }

        public static async Task<Processor> UpdateProcessorAsync(string BaseAddress, Processor processor)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, "processor/", processor.Name));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Put, uri);
            message.Content = new StringContent(JsonConvert.SerializeObject(processor), Encoding.UTF8, "application/json");
            return await GetHttpResponseAsProcessorAsync(await client.SendAsync(message));
        }

        public static async Task<Processor> AddProcessorAsync(string BaseAddress, Processor processor)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, "processor/", processor.Name));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, uri);
            message.Content = new StringContent(JsonConvert.SerializeObject(processor), Encoding.UTF8, "application/json");
            return await GetHttpResponseAsProcessorAsync(await client.SendAsync(message));
        }

        public static async Task<Processor> GetProcessorAsync(string BaseAddress, string ProcessorName)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, "processor/", ProcessorName));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, uri);
            return await GetHttpResponseAsProcessorAsync(await client.SendAsync(message));
        }

        public static async Task<Processor[]> GetAllProcessorsAsync(string BaseAddress)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, "processor/"));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, uri);
            return await GetHttpResponseAsProcessorsAsync(await client.SendAsync(message));
        }

        public static async Task<Processor> DeleteProcessorAsync(string BaseAddress, string processorName)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, "processor/", processorName));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Delete, uri);
            return await GetHttpResponseAsProcessorAsync(await client.SendAsync(message));
        }

#if DEBUG
        /*
            the following function should be removed from the final deployment
            it is only used to send test messages to event hub
        */


        private static async Task SendToEventsToEventHubAsync(
            int NumberOfMessages,
            string PublisherName,
            EventHubDefinition HubDefinition)
        {
            string EventHubConnectionString = HubDefinition.ConnectionString;
            string EventHubName = HubDefinition.EventHubName;

            EventHubClient eventHubClient = EventHubClient.CreateFromConnectionString(EventHubConnectionString, EventHubName);
            int current = 1;
            Random rand = new Random();

            do
            {
                var Event = new
                {
                    DeviceId = PublisherName,
                    FloorId = string.Concat("f", rand.Next(1, 10).ToString()),
                    BuildingId = string.Concat("b", rand.Next(1, 10).ToString()),
                    TempF = rand.Next(1, 100).ToString(),
                    Humidity = rand.Next(1, 100).ToString(),
                    Motion = rand.Next(1, 10).ToString(),
                    light = rand.Next(1, 10).ToString(),
                    EventDate = DateTime.UtcNow.ToString()
                };

                // Powershell redirects stdout to PS console.
                Console.WriteLine(
                    string.Format("sending message# {0}/{1} for Publisher {2} Hub:{3}", current, NumberOfMessages, PublisherName, HubDefinition.EventHubName));
                EventData ev = new EventData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Event)));
                ev.SystemProperties[EventDataSystemPropertyNames.Publisher] = PublisherName;
                await eventHubClient.SendAsync(ev);
                current++;
            } while (current <= NumberOfMessages);
        }

        public static async Task SendTestEventsToProcessorHubsAsync(
            Processor processor,
            int NumOfMessages,
            int NumOfPublishers)
        {
            string PublisherNameFormat = "sensor{0}";
            int NumberOfMessagesPerPublisher = NumOfMessages/NumOfPublishers;
            List<Task> tasks = new List<Task>();

            for (int i = 1; i <= NumOfPublishers; i++)
            {
                foreach (EventHubDefinition hub in processor.Hubs)
                {
                    string publisherName = string.Format(PublisherNameFormat, i);
                    tasks.Add(SendToEventsToEventHubAsync(NumberOfMessagesPerPublisher, publisherName, hub));
                }
            }
            await Task.WhenAll(tasks);
        }

#endif

        #region helpers

        private static async Task<Processor> GetHttpResponseAsProcessorAsync(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            string sJson = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<Processor>(sJson);
        }

        private static async Task<Processor[]> GetHttpResponseAsProcessorsAsync(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            string sJson = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<Processor[]>(sJson);
        }

        private static async Task<ProcessorRuntimeStatus[]> GetHttpResponseAsRuntimeStatusAsync(HttpResponseMessage response)
        {
            response.EnsureSuccessStatusCode();
            string sJson = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<ProcessorRuntimeStatus[]>(sJson);
        }

        private static async Task doPreProcessing(HttpClient client)
        {
            // todo: wireup authN headers 
            await Task.Delay(0);
        }

        #endregion

        #region Per Worker Action

        public static async Task<Processor> StopProcessorAsync(string BaseAddress, string processorName)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, string.Format("processor/{0}/stop", processorName)));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, uri);

            return await GetHttpResponseAsProcessorAsync(await client.SendAsync(message));
        }

        public static async Task<Processor> DrainStopProcessorAsync(string BaseAddress, string processorName)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, string.Format("processor/{0}/drainstop", processorName)));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, uri);
            return await GetHttpResponseAsProcessorAsync(await client.SendAsync(message));
        }


        public static async Task<Processor> PauseProcessorAsync(string BaseAddress, string processorName)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, string.Format("processor/{0}/pause", processorName)));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, uri);
            return await GetHttpResponseAsProcessorAsync(await client.SendAsync(message));
        }


        public static async Task<Processor> ResumeProcessorAsync(string BaseAddress, string processorName)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, string.Format("processor/{0}/resume", processorName)));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, uri);
            return await GetHttpResponseAsProcessorAsync(await client.SendAsync(message));
        }


        public static async Task<ProcessorRuntimeStatus[]> GetDetailedProcessorStatusAsync(string BaseAddress, string processorName)
        {
            Uri uri = new Uri(string.Concat(BaseAddress, string.Format("processor/{0}/detailed", processorName)));
            HttpClient client = new HttpClient();
            await doPreProcessing(client);

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, uri);
            return await GetHttpResponseAsRuntimeStatusAsync(await client.SendAsync(message));
        }

        #endregion
    }
}