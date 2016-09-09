// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Admin.WebService.Models
{
    public class TenantApplicationParams
    {
        public TenantApplicationParams(int dataPartitionCount, int webInstanceCount, string version)
        {
            this.DataPartitionCount = dataPartitionCount;
            this.WebInstanceCount = webInstanceCount;
            this.Version = version;
        }
        
        public int DataPartitionCount { get; set; }

        public int WebInstanceCount { get; set; }

        public string Version { get; set; }
    }
}