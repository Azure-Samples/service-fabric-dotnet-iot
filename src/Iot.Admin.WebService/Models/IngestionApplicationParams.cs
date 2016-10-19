// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Admin.WebService.Models
{
    public class IngestionApplicationParams
    {
        public IngestionApplicationParams(string iotHubConnectionString, int partitionCount, string version)
        {
            this.IotHubConnectionString = iotHubConnectionString;
            this.Version = version;
        }

        public string IotHubConnectionString { get; set; }
        
        public string Version { get; set; }
    }
}