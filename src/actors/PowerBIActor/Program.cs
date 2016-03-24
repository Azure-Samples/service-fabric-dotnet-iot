// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PowerBIActor
{
    using System;
    using System.Fabric;
    using System.Threading;
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.ServiceFabric.Actors.Runtime;

    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                ActorRuntime.RegisterActorAsync<PowerBIActor>().GetAwaiter().GetResult();

                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                ActorEventSource.Current.ActorHostInitializationFailed(e);
                throw;
            }
        }
    }
}