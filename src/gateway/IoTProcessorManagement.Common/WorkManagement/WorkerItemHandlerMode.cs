// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    public enum WorkItemHandlerMode
    {
        /// <summary>
        /// one Handler will be created for all Work Items 
        /// </summary>
        Singlton,

        /// <summary>
        /// one Handler will be created for every queue of work items
        /// </summary>
        PerQueue,

        /// <summary>
        /// one Handler will be created for every work item handled
        /// </summary>
        PerWorkItem
    }
}