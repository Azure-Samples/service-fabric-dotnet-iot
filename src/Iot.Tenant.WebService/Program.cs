// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.WebService
{
    using System.Threading;
    using Microsoft.ServiceFabric.Services.Runtime;

    public class Program
    {
        // Entry point for the application.
        public static void Main(string[] args)
        {
#if LOCALSERVER

            using (LocalServer listener = new LocalServer())
            {
                listener.Open();
            }

#else

            ServiceRuntime.RegisterServiceAsync(
                "WebServiceType",
                context =>
                    new WebService(context)).GetAwaiter().GetResult();

            Thread.Sleep(Timeout.Infinite);
#endif
        }
    }
}