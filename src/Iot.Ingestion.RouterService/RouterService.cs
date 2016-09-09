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

    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
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
            string connectionString =
                this.Context.CodePackageActivationContext
                    .GetConfigurationPackageObject("Config")
                    .Settings
                    .Sections["IoTHubConfigInformation"]
                    .Parameters["ConnectionString"]
                    .Value;

            IReliableDictionary<long, string> offsetDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<long, string>>("OffsetDictionary");
            IReliableDictionary<string, long> epochDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>("EpochDictionary");

            // Each partition of this service corresponds to a partition in IoT Hub.
            // IoT Hub partitions are numbered 1-n, up to 32.
            // This service needs to use the same partitioning scheme, 
            // then for the current partition, grab the low key and use that as the IoT Hub partition key.
            Int64RangePartitionInformation partitionInfo = (Int64RangePartitionInformation) this.Partition.PartitionInfo;
            long partitionKey = partitionInfo.LowKey;

            EventHubReceiver eventHubReceiver = await this.GetEventHubClient(connectionString, partitionKey, epochDictionary, offsetDictionary);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    EventData eventData = await eventHubReceiver.ReceiveAsync(TimeSpan.FromMilliseconds(500));

                    // Message format:
                    // <header><body>
                    // <header> : <7-bit-encoded-int-header-length>tenantId;deviceId
                    // <body> : JSON payload

                    if (eventData != null)
                    {
                        using (Stream eventStream = eventData.GetBodyStream())
                        {
                            using (BinaryReader reader = new BinaryReader(eventStream))
                            {
                                string header = reader.ReadString();
                                int delimeter = header.IndexOf(';');

                                string tenantId = header.Substring(0, delimeter);
                                string deviceId = header.Substring(delimeter + 1);

                                Uri tenantServiceName = new Uri($"{Names.TenantApplicationNamePrefix}/{tenantId}/{Names.TenantDataServiceName}");
                                long tenantServicePartitionKey = FnvHash.Hash(deviceId);

                                Uri postUrl = new HttpServiceUriBuilder()
                                    .SetServiceName(tenantServiceName)
                                    .SetPartitionKey(tenantServicePartitionKey)
                                    .SetServicePathAndQuery($"/api/events/{deviceId}")
                                    .Build();

                                HttpClient httpClient = new HttpClient(new HttpServiceClientHandler());

                                using (StreamContent postContent = new StreamContent(eventStream))
                                {
                                    await httpClient.PostAsync(postUrl, postContent, cancellationToken);
                                }

                                ServiceEventSource.Current.ServiceMessage(
                                    this.Context,
                                    "Sent event data to tenant service '{0}' with partition key '{1}'",
                                    tenantServiceName,
                                    tenantServicePartitionKey);
                            }
                        }

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
                ConditionalValue<string> offsetResult = await offsetDictionary.TryGetValueAsync(tx, partitionKey, LockMode.Update);
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
                                .CreateReceiverAsync(partitionKey.ToString(), DateTime.Parse("2016-08-28T17:30:00Z"), newEpoch);
                }

                // epoch is recorded each time the service fails over or restarts.
                await epochDictionary.SetAsync(tx, "epoch", newEpoch);
                await tx.CommitAsync();
            }

            return eventHubReceiver;
        }
    }
}