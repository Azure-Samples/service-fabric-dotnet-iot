// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Ingestion.RouterService
{
    using System;
    using System.Fabric;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Common;
    using Microsoft.ServiceBus.Messaging;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System.Net.Http.Headers;

    /// <summary>
    /// This service continuously pulls from IoT Hub and sends events off to tenant applications.
    /// </summary>
    /// <remarks>
    /// A custom message format is used for each device event stream:
    ///
    /// Message format: [header][body]
    /// [header] : [7-bit-encoded-int-header-length]tenantId;deviceId
    /// [body] : JSON payload
    /// 
    /// The tenant ID and device ID are needed to figure out which tenant to send the event to.
    /// This information is NOT included in the JSON payload, however, 
    /// so that it is not required to deserialize and buffer the entire JSON object in memory in this service.
    /// </remarks>
    internal sealed class RouterService : StatefulService
    {

        public RouterService(StatefulServiceContext context)
            : base(context)
        {
        }

        /// <summary>
        /// This is the main entry point for your service replica.
        /// This method executes when this replica of your service becomes primary and has write status.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            // Get the IoT Hub connection string from the Settings.xml config file
            // from a configuration package named "Config"
            string connectionString =
                this.Context.CodePackageActivationContext
                    .GetConfigurationPackageObject("Config")
                    .Settings
                    .Sections["IoTHubConfigInformation"]
                    .Parameters["ConnectionString"]
                    .Value;

            // These Reliable Dictionaries are used to keep track of our position in IoT Hub.
            // If this service fails over, this will allow it to pick up where it left off in the event stream.
            IReliableDictionary<long, string> offsetDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<long, string>>("OffsetDictionary");
            IReliableDictionary<string, long> epochDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>("EpochDictionary");

            // Each partition of this service corresponds to a partition in IoT Hub.
            // IoT Hub partitions are numbered 0..n-1, up to n = 32.
            // This service needs to use an identical partitioning scheme. 
            // The low key of every partition corresponds to the IoT Hub partition key.
            Int64RangePartitionInformation partitionInfo = (Int64RangePartitionInformation)this.Partition.PartitionInfo;
            long partitionKey = partitionInfo.LowKey;

            EventHubReceiver eventHubReceiver = await this.GetEventHubClient(connectionString, partitionKey, epochDictionary, offsetDictionary);
            HttpClient httpClient = new HttpClient(new HttpServiceClientHandler());

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // It's important to set a low wait time here in lieu of a cancellation token
                    // so that this doesn't block RunAsync from completing when Service Fabric needs it to complete.
                    using (EventData eventData = await eventHubReceiver.ReceiveAsync(TimeSpan.FromMilliseconds(500)))
                    {
                        if (eventData == null)
                        {
                            continue;
                        }

                        using (Stream eventStream = eventData.GetBodyStream())
                        {
                            using (BinaryReader reader = new BinaryReader(eventStream))
                            {
                                // parse out the tenant and device ID from the device event stream
                                string header = reader.ReadString();
                                int delimeter = header.IndexOf(';');

                                string tenantId = header.Substring(0, delimeter);
                                string deviceId = header.Substring(delimeter + 1);

                                // This is the named service instance of the tenant data service that the event should be sent to.
                                // The tenant ID is part of the named service instance name.
                                // The incoming device data stream specifie which tenant the data belongs to.
                                Uri tenantServiceName = new Uri($"{Names.TenantApplicationNamePrefix}/{tenantId}/{Names.TenantDataServiceName}");
                                long tenantServicePartitionKey = FnvHash.Hash(deviceId);

                                // The tenant data service exposes an HTTP API.
                                // For incoming device events, the URL is /api/events/{deviceId}
                                // This sets up a URL and sends a POST request with the device JSON payload.
                                Uri postUrl = new HttpServiceUriBuilder()
                                    .SetServiceName(tenantServiceName)
                                    .SetPartitionKey(tenantServicePartitionKey)
                                    .SetServicePathAndQuery($"/api/events/{deviceId}")
                                    .Build();


                                // The device stream payload isn't deserialized and buffered in memory here.
                                // Instead, we just can just hook the incoming stream from Iot Hub right into the HTTP request.
                                using (StreamContent postContent = new StreamContent(eventStream))
                                {
                                    postContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                                    HttpResponseMessage response = await httpClient.PostAsync(postUrl, postContent, cancellationToken);

                                    ServiceEventSource.Current.ServiceMessage(
                                        this.Context,
                                        "Sent event data to tenant service '{0}' with partition key '{1}'. Result: {2}",
                                        tenantServiceName,
                                        tenantServicePartitionKey,
                                        response.StatusCode.ToString());
                                }
                            }
                        }

                        // Finally, save the current Iot Hub data stream offset.
                        // This will allow the service to pick up from its current location if it fails over.
                        using (ITransaction tx = this.StateManager.CreateTransaction())
                        {
                            await offsetDictionary.SetAsync(tx, partitionKey, eventData.Offset);
                            await tx.CommitAsync();
                        }
                    }

                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, ex.ToString());
                }
            }
        }

        /// <summary>
        /// Creates an EventHubReceiver from the given connection sting and partition key.
        /// The Reliable Dictionaries are used to create a receiver from wherever the service last left off,
        /// or from the current date/time if it's the first time the service is coming up.
        /// </summary>
        /// <param name="connectionString"></param>
        /// <param name="partitionKey"></param>
        /// <param name="epochDictionary"></param>
        /// <param name="offsetDictionary"></param>
        /// <returns></returns>
        private async Task<EventHubReceiver> GetEventHubClient(
            string connectionString,
            long partitionKey,
            IReliableDictionary<string, long> epochDictionary,
            IReliableDictionary<long, string> offsetDictionary)
        {
            EventHubClient eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, "messages/events");
            EventHubReceiver eventHubReceiver;

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<string> offsetResult = await offsetDictionary.TryGetValueAsync(tx, partitionKey, LockMode.Default);
                ConditionalValue<long> epochResult = await epochDictionary.TryGetValueAsync(tx, "epoch", LockMode.Update);

                long newEpoch = epochResult.HasValue
                    ? epochResult.Value + 1
                    : 0;

                if (offsetResult.HasValue)
                {
                    // continue where the service left off before the last failover or restart.
                    ServiceEventSource.Current.ServiceMessage(
                        this.Context,
                        "Creating listener on partitionkey {0} with offset {1}",
                        partitionKey,
                        offsetResult.Value);

                    eventHubReceiver = await eventHubClient.GetDefaultConsumerGroup().CreateReceiverAsync(partitionKey.ToString(), offsetResult.Value, newEpoch);
                }
                else
                {
                    // first time this service is running so there is no offset value yet.
                    // start with the current time.
                    ServiceEventSource.Current.ServiceMessage(
                        this.Context,
                        "Creating listener on partitionkey {0} with offset {1}",
                        partitionKey,
                        DateTime.UtcNow);

                    eventHubReceiver =
                        await
                            eventHubClient.GetDefaultConsumerGroup()
                                .CreateReceiverAsync(partitionKey.ToString(), DateTime.UtcNow, newEpoch);
                }

                // epoch is recorded each time the service fails over or restarts.
                await epochDictionary.SetAsync(tx, "epoch", newEpoch);
                await tx.CommitAsync();
            }

            return eventHubReceiver;
        }
    }
}