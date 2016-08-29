// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.WebService
{
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
    using System.Linq;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System;
    using IoT.Common;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class WebService : StatelessService
    {
        private readonly CancellationTokenSource webApiCancellationSource;

        public WebService(StatelessServiceContext context)
            : base(context)
        {
            this.webApiCancellationSource = new CancellationTokenSource();
        }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[1]
            {
                new ServiceInstanceListener(
                    context =>
                    {
                        string tenantName = new Uri(context.CodePackageActivationContext.ApplicationName).Segments.Last();

                        return new WebHostCommunicationListener(
                            context,
                            tenantName,
                            "ServiceEndpoint",
                            uri => {
                                ServiceEventSource.Current.Message($"Listening on {uri}");

                                return new WebHostBuilder().UseWebListener()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<FabricClient>(new FabricClient())
                                            .AddSingleton<CancellationTokenSource>(this.webApiCancellationSource))
                                   .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseUrls(uri)
                                    .Build();
                            });
                    })
            };
        }

        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => this.webApiCancellationSource.Cancel());

            return Task.FromResult(true);
        }
    }
}