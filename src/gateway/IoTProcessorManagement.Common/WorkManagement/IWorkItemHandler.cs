// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System.Threading.Tasks;

    public interface IWorkItemHandler<Wi> where Wi : IWorkItem
    {
        Task<Wi> HandleWorkItem(Wi workItem);
    }
}