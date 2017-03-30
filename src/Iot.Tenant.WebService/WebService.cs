// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.WebService
{
    using Common;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
    using System.Linq;
    using System.Net.Http;

    internal sealed class WebService : StatelessService
    {
        public WebService(StatelessServiceContext context)
            : base(context)
        {
        }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[1]
            {
                new ServiceInstanceListener(
                    context =>
                        new WebListenerCommunicationListener(
                            context,
                            "ServiceEndpoint",
                            (url, listener) =>
                            {
                                // in this sample, tenant application names always have the form "fabric:/Iot.Tenant/<TenantName>
                                // This extracts the tenant name from the application name and uses it as the web application path.
                                string tenantName = new Uri(context.CodePackageActivationContext.ApplicationName).Segments.Last();
                                url += $"/{tenantName}";

                                ServiceEventSource.Current.Message($"Listening on {url}");

                                return new WebHostBuilder()
                                    .UseWebListener()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton<StatelessServiceContext>(context)
                                            .AddSingleton<FabricClient>(new FabricClient())
                                            .AddSingleton<HttpClient>(new HttpClient(new HttpServiceClientHandler())))
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.None)
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseUrls(url)
                                    .Build();
                            })
                    )
            };
        }

    }
}