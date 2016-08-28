// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.DataService.Models
{
    using System.Collections.Generic;
    using System.Runtime.Serialization;

    [DataContract]
    internal class DeviceEventSeries
    {
        public DeviceEventSeries(string deviceId, IEnumerable<DeviceEvent> events)
        {
            this.DeviceId = deviceId;
            this.Events = events;
        }
        
        [DataMember]
        public string DeviceId { get; private set; }

        [DataMember]
        public IEnumerable<DeviceEvent> Events { get; private set; }
    }
}