// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    public enum WorkManagerStatus
    {
        /// <summary>
        /// Just Created
        /// </summary>
        New,

        /// <summary>
        /// Currently working accepting new Work Items
        /// Working on current queued work items
        /// </summary>
        Working,

        /// <summary>
        /// Not working, not accepting working items
        /// </summary>
        Paused,

        /// <summary>
        /// not working, not accepting work items, wil not accept resume commands
        /// </summary>
        Stopped,

        /// <summary>
        /// working on currently queued work items
        /// but will not accept new work items
        /// </summary>
        Draining
    }
}