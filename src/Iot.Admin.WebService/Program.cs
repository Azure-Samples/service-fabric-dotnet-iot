// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Admin.WebService
{
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Description;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;

    public class Program
    {
        // Entry point for the application.
        public static void Main(string[] args)
        {
            ServiceRuntime.RegisterServiceAsync("WebServiceType", context => new WebHostingService(context, "ServiceEndpoint")).GetAwaiter().GetResult();

            Thread.Sleep(Timeout.Infinite);
        }

        /// <summary>
        /// A specialized stateless service for hosting ASP.NET Core web apps.
        /// </summary>
        internal sealed class WebHostingService : StatelessService, ICommunicationListener
        {
            private readonly string _endpointName;

            private IWebHost _webHost;

            public WebHostingService(StatelessServiceContext serviceContext, string endpointName)
                : base(serviceContext)
            {
                this._endpointName = endpointName;
            }

            #region StatelessService

            protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
            {
                return new[] {new ServiceInstanceListener(_ => this)};
            }

            #endregion StatelessService

            #region ICommunicationListener

            void ICommunicationListener.Abort()
            {
                this._webHost?.Dispose();
            }

            Task ICommunicationListener.CloseAsync(CancellationToken cancellationToken)
            {
                this._webHost?.Dispose();

                return Task.FromResult(true);
            }

            Task<string> ICommunicationListener.OpenAsync(CancellationToken cancellationToken)
            {
                EndpointResourceDescription endpoint = FabricRuntime.GetActivationContext().GetEndpoint(this._endpointName);

                string serverUrl = $"{endpoint.Protocol}://{FabricRuntime.GetNodeContext().IPAddressOrFQDN}:{endpoint.Port}";

                this._webHost = new WebHostBuilder().UseKestrel()
                    .UseContentRoot(Directory.GetCurrentDirectory())
                    .UseStartup<Startup>()
                    .UseUrls(serverUrl)
                    .Build();

                this._webHost.Start();

                return Task.FromResult(serverUrl);
            }

            #endregion ICommunicationListener
        }
    }
}