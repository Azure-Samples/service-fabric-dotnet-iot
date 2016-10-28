// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.WebService
{
    using System;
    using System.Fabric;
    using System.IO;
    using System.Threading;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;

    public class LocalServer : IDisposable
    {
        private IWebHost webHost;

        public void Dispose()
        {
            this.webHost?.Dispose();
        }

        public void Open()
        {
            string serverUrl = "http://localhost:5001/iot";

            CancellationTokenSource webApiCancellationSource = new CancellationTokenSource();
            FabricClient fabricClient = new FabricClient();

            this.webHost = new WebHostBuilder().UseKestrel()
                .ConfigureServices(
                    services => services
                        .AddSingleton<FabricClient>(fabricClient)
                        .AddSingleton<CancellationTokenSource>(webApiCancellationSource))
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>()
                .UseUrls(serverUrl)
                .Build();

            this.webHost.Run();
        }
    }
}