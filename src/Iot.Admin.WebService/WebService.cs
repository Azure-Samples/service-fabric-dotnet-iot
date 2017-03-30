// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Admin.WebService
{
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using Microsoft.ServiceFabric.Services.Communication.AspNetCore;

    internal sealed class WebService : StatelessService
    {
        private readonly FabricClient fabricClient;

        public WebService(StatelessServiceContext context)
            : base(context)
        {
            this.fabricClient = new FabricClient();
        }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[1]
            {
                new ServiceInstanceListener(
                    context =>
                    {
                        return new WebListenerCommunicationListener(
                            context,
                            "ServiceEndpoint",
                            (url, listener) =>
                            {
                                url += "/iot";

                                ServiceEventSource.Current.Message($"Admin WebService starting on {url}");

                                return new WebHostBuilder().UseWebListener()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<FabricClient>(this.fabricClient))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                    .UseStartup<Startup>()
                                    .UseUrls(url)
                                    .Build();
                            });
                    })
            };
        }
    }
}