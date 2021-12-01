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
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Iot.Common;
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Processor;
    using Azure.Storage.Blobs;
    using Azure.Messaging.EventHubs.Consumer;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System.Diagnostics;
    using System.Text.RegularExpressions;

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
            var storageConnectionString = this.Context.CodePackageActivationContext
                    .GetConfigurationPackageObject("Config")
                    .Settings
                    .Sections["StorageConfigInformation"]
                    .Parameters["StorageConnectionString"]
                    .Value;
            var blobContainerName = this.Context.CodePackageActivationContext
                    .GetConfigurationPackageObject("Config")
                    .Settings
                    .Sections["StorageConfigInformation"]
                    .Parameters["BlobContainerName"]
                    .Value;

            var eventHubsConnectionString = Regex.Split(iotHubConnectionString, ";EntityPath=", RegexOptions.IgnoreCase)[0];
            var eventHubName = Regex.Split(iotHubConnectionString, ";EntityPath=", RegexOptions.IgnoreCase)[1];
            var consumerGroup = EventHubConsumerClient.DefaultConsumerGroupName;

            var storageClient = new BlobContainerClient(
                storageConnectionString,
                blobContainerName);

            var processor = new EventProcessorClient(
                storageClient,
                consumerGroup,
                eventHubsConnectionString,
                eventHubName);

            async Task processEventHandler(ProcessEventArgs args)
            {
                try
                {
                    // If the cancellation token is signaled, then the
                    // processor has been asked to stop.  It will invoke
                    // this handler with any events that were in flight;
                    // these will not be lost if not processed.
                    //
                    // It is up to the handler to decide whether to take
                    // action to process the event or to cancel immediately.

                    if (args.CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    using (HttpClient httpClient = new HttpClient(new HttpServiceClientHandler()))
                    {
                        int offsetIteration = 0;
                        if (args.Data == null)
                        {
                            return;
                        }

                        string tenantId = (string)args.Data.Properties["TenantID"];
                        string deviceId = (string)args.Data.Properties["DeviceID"];

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
                        using (Stream eventStream = args.Data.BodyAsStream)
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
                                    args.Data.Offset);

                            using (ITransaction tx = this.StateManager.CreateTransaction())
                            {
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

            Task processErrorHandler(ProcessErrorEventArgs args)
            {
                try
                {
                    Debug.WriteLine("Error in the EventProcessorClient");
                    Debug.WriteLine($"\tOperation: { args.Operation }");
                    Debug.WriteLine($"\tException: { args.Exception }");
                    Debug.WriteLine("");
                }
                catch (Exception ex)
                {
                    ServiceEventSource.Current.ServiceMessage(this.Context, ex.ToString());
                    throw;
                }
                return Task.CompletedTask;
            }

            try
            {
                using var cancellationSource = new CancellationTokenSource();
                cancellationSource.CancelAfter(TimeSpan.FromSeconds(30));

                processor.ProcessEventAsync += processEventHandler;
                processor.ProcessErrorAsync += processErrorHandler;

                try
                {
                    await processor.StartProcessingAsync(cancellationSource.Token);
                    await Task.Delay(Timeout.Infinite, cancellationSource.Token);
                }
                catch (TaskCanceledException)
                {
                    // This is expected if the cancellation token is
                    // signaled.
                }
                finally
                {
                    // This may take up to the length of time defined
                    // as part of the configured TryTimeout of the processor;
                    // by default, this is 60 seconds.

                    await processor.StopProcessingAsync();
                }
            }
            catch
            {
                // The processor will automatically attempt to recover from any
                // failures, either transient or fatal, and continue processing.
                // Errors in the processor's operation will be surfaced through
                // its error handler.
                //
                // If this block is invoked, then something external to the
                // processor was the source of the exception.
            }
            finally
            {
                // It is encouraged that you unregister your handlers when you have
                // finished using the Event Processor to ensure proper cleanup.  This
                // is especially important when using lambda expressions or handlers
                // in any form that may contain closure scopes or hold other references.

                processor.ProcessEventAsync -= processEventHandler;
                processor.ProcessErrorAsync -= processErrorHandler;
            }
        }
    }
}