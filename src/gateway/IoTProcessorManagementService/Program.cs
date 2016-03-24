// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System;
    using System.Diagnostics;
    using System.Fabric;
    using System.Threading;
    using Microsoft.ServiceFabric.Services.Runtime;
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                ServiceRuntime.RegisterServiceAsync("ProcessorManagementServiceType", context =>
                    new ProcessorManagementService(context)).GetAwaiter().GetResult();
                
                ServiceEventSource.Current.ServiceTypeRegistered(Process.GetCurrentProcess().Id, typeof(ProcessorManagementService).Name);

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