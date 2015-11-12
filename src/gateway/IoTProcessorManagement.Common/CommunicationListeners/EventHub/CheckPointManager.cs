// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System.Threading.Tasks;
    using Microsoft.ServiceBus.Messaging;

    /// <summary>
    /// vanila implementation of ICheckpointManager (refer to Event Hub SDK).
    /// the check point manager sets the lease and presist it in Service Fabric state.
    /// </summary>
    internal class CheckPointManager : ICheckpointManager
    {
        public async Task CheckpointAsync(Lease lease, string offset, long sequenceNumber)
        {
            StateManagerLease stateManagerLease = lease as StateManagerLease;

            stateManagerLease.Offset = offset;
            stateManagerLease.SequenceNumber = sequenceNumber;
            await stateManagerLease.SaveAsync();
        }
    }
}