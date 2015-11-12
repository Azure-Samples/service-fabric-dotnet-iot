// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System;

    public enum WorkerManagerClickType
    {
        Posted,
        Processed
    }

    internal class WorkManagerClick : IClick<int>, ICloneable
    {
        public WorkerManagerClickType ClickType { get; set; }

        public IClick<int> Next { get; set; }

        public int Value { get; set; }

        public long When { get; set; }

        public object Clone()
        {
            return new WorkManagerClick()
            {
                ClickType = this.ClickType,
                Value = this.Value,
                When = this.When
            };
        }
    }
}