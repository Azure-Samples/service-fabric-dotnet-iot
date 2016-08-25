// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Ingestion.RouterService
{
    using System;
    using System.Fabric;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Common;
    using Microsoft.ServiceBus.Messaging;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Runtime;
    using Newtonsoft.Json;
    using System.Net.Http;

    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class RouterService : StatefulService
    {
        internal const string TenantApplicationNamePrefix = "fabric:/Tenant/";
        internal const string TenantDataServiceName = "DataService";

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
                this.Context.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings.Sections["IoTHubConfigInformation"].Parameters[
                    "Endpoint"].Value;
            EventHubClient eventHubClient = EventHubClient.CreateFromConnectionString(connectionString, "messages/events");

            Int64RangePartitionInformation partitionInfo = this.Partition.PartitionInfo as Int64RangePartitionInformation;
            long partitionKey = partitionInfo.LowKey;

            IReliableDictionary<long, string> offsetDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<long, string>>("OffsetDictionary");
            IReliableDictionary<string, long> epochDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>("EpochDictionary");

            EventHubReceiver eventHubReceiver;

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<string> offsetResult = await offsetDictionary.TryGetValueAsync(tx, partitionInfo.LowKey, LockMode.Update);
                ConditionalValue<long> epochResult = await epochDictionary.TryGetValueAsync(tx, "epoch", LockMode.Update);

                long newEpoch;

                if (epochResult.HasValue)
                {
                    long oldEpoch = epochResult.Value;
                    newEpoch = oldEpoch++;
                }
                else
                {
                    newEpoch = 0;
                }

                if (offsetResult.HasValue)
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this.Context,
                        "Creating listener on partitionkey {0} with offset {1}",
                        partitionKey,
                        offsetResult.Value);
                    eventHubReceiver = await eventHubClient.GetConsumerGroup("sf2").CreateReceiverAsync(partitionKey.ToString(), offsetResult.Value, newEpoch);
                }
                else
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this.Context,
                        "Creating listener on partitionkey {0} with offset {1}",
                        partitionKey,
                        DateTime.UtcNow);
                    eventHubReceiver = await eventHubClient.GetConsumerGroup("sf2").CreateReceiverAsync(partitionKey.ToString(), DateTime.UtcNow, newEpoch);
                }

                await epochDictionary.SetAsync(tx, "epoch", newEpoch);

                await tx.CommitAsync();
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                EventData eventData = await eventHubReceiver.ReceiveAsync();

                // Message format:
                // 16-bit-header-length<header><body>
                // <header> : tenantId;deviceId
                // <body> : JSON payload

                if (eventData != null)
                {
                    string tenantId;
                    string deviceId;

                    using (Stream eventStream = eventData.GetBodyStream())
                    {
                        using (BinaryReader reader = new BinaryReader(eventStream, Encoding.UTF8, true))
                        {
                            short headerLength = reader.ReadInt16();
                            string header = Encoding.UTF8.GetString(reader.ReadBytes(headerLength));
                            int delimeter = header.IndexOf(';');
                            tenantId = header.Substring(0, delimeter);
                            deviceId = header.Substring(delimeter + 1);
                        }

                        Uri tenantServiceName = new Uri($"{TenantApplicationNamePrefix}/{tenantId}/{TenantDataServiceName}");
                        long tenantServicePartitionKey = (long)FnvHash.Hash(Encoding.UTF8.GetBytes(deviceId));

                        HttpClient httpClient = new HttpClient(new HttpServiceClientHandler());

                        Uri postUrl = new HttpServiceUriBuilder()
                            .SetServiceName(tenantServiceName)
                            .SetPartitionKey(tenantServicePartitionKey)
                            .SetServicePathAndQuery("/api/events")
                            .Build();

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

                    using (ITransaction tx = this.StateManager.CreateTransaction())
                    {
                        await offsetDictionary.SetAsync(tx, partitionKey, eventData.Offset);
                        await tx.CommitAsync();
                    }
                }
            }
        }
    }
}