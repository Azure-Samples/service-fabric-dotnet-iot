// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    /// <summary>
    /// describes how are Event Hub partitions are 
    /// distributed on Service Fabric stateful service partitions
    /// </summary>
    public enum EventHubCommunicationListenerMode
    {
        /// <summary>
        /// Maps 1..n Event Hub Partitions to 1 Service Fabric partition. 
        /// will throw an exception if service fabric partitions are > Event hub partition
        /// </summary>
        SafeDistribute,

        /// <summary>
        /// maps 1..n event hub partitions to 1 service fabric partition
        /// if service fabric partitions are > event hub partitions, the remaining partitions will not
        /// get any distribution (i.e Event processor will not be created on them).
        /// </summary>
        Distribute,

        /// <summary>
        ///  maps one to one event hub partition: Service Fabric Partition
        /// Service Fabric partition has to = Event Hub Partitions
        /// </summary>
        OneToOne,

        /// <summary>
        /// maps a single event hub partition to a single service fabric partition. 
        /// Event Hub communication listener will expect a supplied valid event hub partition id
        /// </summary>
        Single
    }
}