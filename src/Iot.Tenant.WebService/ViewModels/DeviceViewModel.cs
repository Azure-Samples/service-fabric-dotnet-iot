// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Tenant.WebService.ViewModels
{
    using System;

    public class DeviceViewModel
    {
        public DeviceViewModel(string id, DateTimeOffset timestamp)
        {
            this.Id = id;
            this.Timestamp = timestamp;
        }

        public string Id { get; private set; }

        public DateTimeOffset Timestamp { get; private set; }
    }
}