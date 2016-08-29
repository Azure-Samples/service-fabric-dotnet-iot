// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.DataService
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using Models;
    using IoT.Common;
    using Controllers;

    internal sealed class DataService : StatefulService
    {
        internal const string EventDictionaryName = "store://events/dictionary";
        internal const string EventQueueName = "store://events/queue";
        private const int OffloadBatchSize = 100;
        private readonly TimeSpan OffloadBatchInterval = TimeSpan.FromSeconds(10);

        private readonly CancellationTokenSource webApiCancellationSource;

        public DataService(StatefulServiceContext context)
            : base(context)
        {
            this.webApiCancellationSource = new CancellationTokenSource();
        }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new ServiceReplicaListener[1]
            {
                new ServiceReplicaListener(
                    context =>
                    {
                        return new WebHostCommunicationListener(
                            context,
                            "ServiceEndpoint",
                            uri =>
                            {
                                ServiceEventSource.Current.Message($"Listening on {uri}");

                                return new WebHostBuilder().UseWebListener()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<IReliableStateManager>(this.StateManager)
                                            .AddSingleton<CancellationTokenSource>(this.webApiCancellationSource))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseUrls(uri)
                                    .Build();
                            });
                    })
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => this.webApiCancellationSource.Cancel());

            IReliableQueue<DeviceEventSeries> queue = await this.StateManager.GetOrAddAsync<IReliableQueue<DeviceEventSeries>>(EventQueueName);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using (ITransaction tx = this.StateManager.CreateTransaction())
                    {
                        // When the number of items in the queue reaches a certain size..
                        long count = await queue.GetCountAsync(tx);

                        ServiceEventSource.Current.ServiceMessage(this.Context, $"Current queue size: {count}");

                        if (count >= OffloadBatchSize)
                        {
                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Starting batch offload..");

                            // Dequeue the items into a batch
                            List<DeviceEventSeries> batch = new List<DeviceEventSeries>(OffloadBatchSize);

                            for (int i = 0; i < OffloadBatchSize; ++i)
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                ConditionalValue<DeviceEventSeries> result = await queue.TryDequeueAsync(tx);

                                if (result.HasValue)
                                {
                                    batch.Add(result.Value);
                                }
                            }

                            // Commit the dequeue operations
                            await tx.CommitAsync();

                            ServiceEventSource.Current.ServiceMessage(this.Context, $"Batch offload complete.");
                        }
                    }
                }
                catch (TimeoutException)
                {
                    // transient error. Retry.
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"TimeoutException in RunAsync.");
                }
                catch (FabricTransientException)
                {
                    // transient error. Retry.
                    ServiceEventSource.Current.ServiceMessage(this.Context, $"FabricTransientException in RunAsync.");
                }
                catch (FabricNotPrimaryException)
                {
                    // not primary any more, time to quit.
                    return;
                }

                await Task.Delay(this.OffloadBatchInterval, cancellationToken);
            }
        }
    }
}