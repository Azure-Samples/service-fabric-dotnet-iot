// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Ingestion.RouterService
{
    using System;
    using System.Fabric;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Common;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Runtime;

    /// <summary>
    /// This service continuously pulls from IoT Hub and sends events off to tenant applications.
    /// </summary>
    /// <remarks>
    /// </remarks>
    internal sealed class RouterService : StatefulService
    {
        /// <summary>
        /// The offset interval specifies how frequently the offset is saved.
        /// A lower value will save more often which can reduce repeat message processing at the cost of performance. 
        /// </summary>
        private const int OffsetInterval = 5;

        /// <summary>
        /// Names of the dictionaries that hold the current offset value and partition epoch.
        /// </summary>
        private const string OffsetDictionaryName = "OffsetDictionary";
        private const string EpochDictionaryName = "EpochDictionary";

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
            string iotHubConnectionString =
                this.Context.CodePackageActivationContext
                    .GetConfigurationPackageObject("Config")
                    .Settings
                    .Sections["IoTHubConfigInformation"]
                    .Parameters["ConnectionString"]
                    .Value;

            // These Reliable Dictionaries are used to keep track of our position in IoT Hub.
            // If this service fails over, this will allow it to pick up where it left off in the event stream.
            IReliableDictionary<string, string> offsetDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(OffsetDictionaryName);

            IReliableDictionary<string, long> epochDictionary =
                await this.StateManager.GetOrAddAsync<IReliableDictionary<string, long>>(EpochDictionaryName);

            // Each partition of this service corresponds to a partition in IoT Hub.
            // IoT Hub partitions are numbered 0..n-1, up to n = 32.
            // This service needs to use an identical partitioning scheme. 
            // The low key of every partition corresponds to an IoT Hub partition.
            Int64RangePartitionInformation partitionInfo = (Int64RangePartitionInformation)this.Partition.PartitionInfo;
            long servicePartitionKey = partitionInfo.LowKey;

            EventHubReceiver eventHubReceiver = null;
            MessagingFactory messagingFactory = null;

            try
            {
                // Get an EventHubReceiver and the MessagingFactory used to create it.
                // The EventHubReceiver is used to get events from IoT Hub.
                // The MessagingFactory is just saved for later so it can be closed before RunAsync exits.
                Tuple<EventHubReceiver, MessagingFactory> iotHubInfo =
                    await this.ConnectToIoTHubAsync(iotHubConnectionString, servicePartitionKey, epochDictionary, offsetDictionary);

                eventHubReceiver = iotHubInfo.Item1;
                messagingFactory = iotHubInfo.Item2;

                // HttpClient is designed as a shared object. 
                // A single instance should be used throughout the lifetime of RunAsync.
                using (HttpClient httpClient = new HttpClient(new HttpServiceClientHandler()))
                {

                    int offsetIteration = 0;

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            // It's important to set a low wait time here in lieu of a cancellation token
                            // so that this doesn't block RunAsync from exiting when Service Fabric needs it to complete.
                            // ReceiveAsync is a long-poll operation, so the timeout should not be too low,
                            // yet not too high to block RunAsync from exiting within a few seconds.
                            using (EventData eventData = await eventHubReceiver.ReceiveAsync(TimeSpan.FromSeconds(5)))
                            {
                                if (eventData == null)
                                {
                                    continue;
                                }

                                string tenantId = (string)eventData.Properties["TenantID"];
                                string deviceId = (string)eventData.Properties["DeviceID"];

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
                                // Instead, we just can just hook the incoming stream from Iot Hub right into the HTTP request stream.
                                using (Stream eventStream = eventData.GetBodyStream())
                                {
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

                                        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                                        {
                                            // This service expects the receiving tenant service to return HTTP 400 if the device message was malformed.
                                            // In this example, the message is simply logged.
                                            // Your application should handle all possible error status codes from the receiving service
                                            // and treat the message as a "poison" message.
                                            // Message processing should be allowed to continue after a poison message is detected.

                                            string responseContent = await response.Content.ReadAsStringAsync();

                                            ServiceEventSource.Current.ServiceMessage(
                                                this.Context,
                                                "Tenant service '{0}' returned HTTP 400 due to a bad device message from device '{1}'. Error message: '{2}'",
                                                tenantServiceName,
                                                deviceId,
                                                responseContent);
                                        }
                                    }
                                }

                                // Save the current Iot Hub data stream offset.
                                // This will allow the service to pick up from its current location if it fails over.
                                // Duplicate device messages may still be sent to the the tenant service 
                                // if this service fails over after the message is sent but before the offset is saved.
                                if (++offsetIteration % OffsetInterval == 0)
                                {
                                    ServiceEventSource.Current.ServiceMessage(
                                            this.Context,
                                            "Saving offset {0}",
                                            eventData.Offset);

                                    using (ITransaction tx = this.StateManager.CreateTransaction())
                                    {
                                        await offsetDictionary.SetAsync(tx, "offset", eventData.Offset);
                                        await tx.CommitAsync();
                                    }

                                    offsetIteration = 0;
                                }
                            }
                        }
                        catch (TimeoutException te)
                        {
                            // transient error. Retry.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"TimeoutException in RunAsync: {te.ToString()}");
                        }
                        catch (FabricTransientException fte)
                        {
                            // transient error. Retry.
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"FabricTransientException in RunAsync: {fte.ToString()}");
                        }
                        catch (FabricNotPrimaryException)
                        {
                            // not primary any more, time to quit.
                            return;
                        }
                        catch (Exception ex)
                        {
                            ServiceEventSource.Current.ServiceMessage(this.Context, ex.ToString());

                            throw;
                        }
                    }
                }
            }
            finally
            {
                if (messagingFactory != null)
                {
                    await messagingFactory.CloseAsync();
                }
            }
        }

        /// <summary>
        /// Creates an EventHubReceiver from the given connection sting and partition key.
        /// The Reliable Dictionaries are used to create a receiver from wherever the service last left off,
        /// or from the current date/time if it's the first time the service is coming up.
        /// </summary>
        /// <param name="connectionString"></param>
        /// <param name="servicePartitionKey"></param>
        /// <param name="epochDictionary"></param>
        /// <param name="offsetDictionary"></param>
        /// <returns></returns>
        private async Task<Tuple<EventHubReceiver, MessagingFactory>> ConnectToIoTHubAsync(
            string connectionString,
            long servicePartitionKey,
            IReliableDictionary<string, long> epochDictionary,
            IReliableDictionary<string, string> offsetDictionary)
        {

            // EventHubs doesn't support NetMessaging, so ensure the transport type is AMQP.
            ServiceBusConnectionStringBuilder connectionStringBuilder = new ServiceBusConnectionStringBuilder(connectionString);
            connectionStringBuilder.TransportType = TransportType.Amqp;

            ServiceEventSource.Current.ServiceMessage(
                      this.Context,
                      "RouterService connecting to IoT Hub at {0}",
                      String.Join(",", connectionStringBuilder.Endpoints.Select(x => x.ToString())));

            // A new MessagingFactory is created here so that each partition of this service will have its own MessagingFactory.
            // This gives each partition its own dedicated TCP connection to IoT Hub.
            MessagingFactory messagingFactory = MessagingFactory.CreateFromConnectionString(connectionStringBuilder.ToString());
            EventHubClient eventHubClient = messagingFactory.CreateEventHubClient("messages/events");
            EventHubRuntimeInformation eventHubRuntimeInfo = await eventHubClient.GetRuntimeInformationAsync();
            EventHubReceiver eventHubReceiver;

            // Get an IoT Hub partition ID that corresponds to this partition's low key.
            // This assumes that this service has a partition count 'n' that is equal to the IoT Hub partition count and a partition range of 0..n-1.
            // For example, given an IoT Hub with 32 partitions, this service should be created with:
            // partition count = 32
            // partition range = 0..31
            string eventHubPartitionId = eventHubRuntimeInfo.PartitionIds[servicePartitionKey];

            using (ITransaction tx = this.StateManager.CreateTransaction())
            {
                ConditionalValue<string> offsetResult = await offsetDictionary.TryGetValueAsync(tx, "offset", LockMode.Default);
                ConditionalValue<long> epochResult = await epochDictionary.TryGetValueAsync(tx, "epoch", LockMode.Update);

                long newEpoch = epochResult.HasValue
                    ? epochResult.Value + 1
                    : 0;

                if (offsetResult.HasValue)
                {
                    // continue where the service left off before the last failover or restart.
                    ServiceEventSource.Current.ServiceMessage(
                        this.Context,
                        "Creating EventHub listener on partition {0} with offset {1}",
                        eventHubPartitionId,
                        offsetResult.Value);

                    eventHubReceiver = await eventHubClient.GetDefaultConsumerGroup().CreateReceiverAsync(eventHubPartitionId, offsetResult.Value, newEpoch);
                }
                else
                {
                    // first time this service is running so there is no offset value yet.
                    // start with the current time.
                    ServiceEventSource.Current.ServiceMessage(
                        this.Context,
                        "Creating EventHub listener on partition {0} with offset {1}",
                        eventHubPartitionId,
                        DateTime.UtcNow);

                    eventHubReceiver =
                        await
                            eventHubClient.GetDefaultConsumerGroup()
                                .CreateReceiverAsync(eventHubPartitionId, DateTime.UtcNow, newEpoch);
                }

                // epoch is recorded each time the service fails over or restarts.
                await epochDictionary.SetAsync(tx, "epoch", newEpoch);
                await tx.CommitAsync();
            }

            return new Tuple<EventHubReceiver, MessagingFactory>(eventHubReceiver, messagingFactory);
        }
    }
}