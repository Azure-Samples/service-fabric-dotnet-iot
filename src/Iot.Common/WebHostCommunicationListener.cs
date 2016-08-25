// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoT.Common
{
    using System;
    using System.Fabric;
    using System.Fabric.Description;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;

    public class WebHostCommunicationListener : ICommunicationListener
    {
        private readonly string endpointName;
        private readonly ServiceContext serviceContext;
        private readonly Func<string, IWebHost> build;
        private readonly string appPath;

        private IWebHost webHost;

        public WebHostCommunicationListener(ServiceContext serviceContext, string endpointName, Func<string, IWebHost> build)
            : this(serviceContext, null, endpointName, build)
        {
        }

        public WebHostCommunicationListener(ServiceContext serviceContext, string appPath, string endpointName, Func<string, IWebHost> build)
        {
            this.serviceContext = serviceContext;
            this.endpointName = endpointName;
            this.build = build;
            this.appPath = appPath;
        }

        void ICommunicationListener.Abort()
        {
            this.webHost?.Dispose();
        }

        Task ICommunicationListener.CloseAsync(CancellationToken cancellationToken)
        {
            this.webHost?.Dispose();

            return Task.FromResult(true);
        }

        Task<string> ICommunicationListener.OpenAsync(CancellationToken cancellationToken)
        {
            string ip = this.serviceContext.NodeContext.IPAddressOrFQDN;
            EndpointResourceDescription serviceEndpoint = this.serviceContext.CodePackageActivationContext.GetEndpoint(this.endpointName);
            EndpointProtocol protocol = serviceEndpoint.Protocol;
            int port = serviceEndpoint.Port;
            string host = "+";

            string listenUrl;
            string path = this.appPath != null ? this.appPath.TrimEnd('/') + "/" : "";

            if (this.serviceContext is StatefulServiceContext)
            {
                StatefulServiceContext statefulContext = this.serviceContext as StatefulServiceContext;

                listenUrl =
                    $"{serviceEndpoint.Protocol}://{host}:{serviceEndpoint.Port}/{path}{statefulContext.PartitionId}/{statefulContext.ReplicaId}/{Guid.NewGuid()}";
            }
            else
            {
                listenUrl = $"{serviceEndpoint.Protocol}://{host}:{serviceEndpoint.Port}/{path}";
            }

            this.webHost = this.build(listenUrl);
            this.webHost.Start();

            return Task.FromResult(listenUrl.Replace("://+", "://" + ip));
        }
    }
}