// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.DataService.Models
{
    using System;
    using System.Runtime.Serialization;

    [DataContract]
    internal class DeviceEvent
    {
        public DeviceEvent(DateTimeOffset timestamp)
        {
            this.Timestamp = timestamp;
        }

        [DataMember]
        public DateTimeOffset Timestamp { get; private set; }
    }
}