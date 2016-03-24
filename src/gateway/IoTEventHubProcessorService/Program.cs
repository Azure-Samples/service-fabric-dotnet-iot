// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace EventHubProcessor
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Microsoft.ServiceFabric.Services.Runtime;

    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                ServiceRuntime.RegisterServiceAsync(
                    "IoTEventHubProcessorService",
                    context =>
                        new IoTEventHubProcessorService(context)).GetAwaiter().GetResult();

                ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(IoTEventHubProcessorService).Name);

                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e);
                throw;
            }
        }
    }
}