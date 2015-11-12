// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    public interface IClick<ValueT>
    {
        long When { get; set; }

        IClick<ValueT> Next { get; set; }

        ValueT Value { get; set; }
    }
}