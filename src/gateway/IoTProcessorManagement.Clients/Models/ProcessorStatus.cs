// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Clients
{
    using System;

    [Flags]
    public enum ProcessorStatus : int
    {
        New = 1 << 1,
        Provisioned = 1 << 2,
        PendingDelete = 1 << 3,
        Deleted = 1 << 4,
        PendingPause = 1 << 5,
        Paused = 1 << 6,
        PendingStop = 1 << 7,
        Stopped = 1 << 8,
        PendingResume = 1 << 9,
        PendingDrainStop = 1 << 10,
        ProvisionError = 1 << 11,
        PendingUpdate = 1 << 12,
        Updated = 1 << 13,
    }
}