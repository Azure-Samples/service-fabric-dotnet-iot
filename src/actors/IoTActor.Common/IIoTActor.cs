// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTActor.Common
{
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Actors;

    public interface IIoTActor : IActor
    {
        Task Post(string DeviceId, string EventHubName, string ServiceBusNS, byte[] Body);
    }
}