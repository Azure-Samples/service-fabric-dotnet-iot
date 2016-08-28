using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Iot.Admin.WebService.Models
{
    public class IngestionApplicationParams
    {
        public IngestionApplicationParams(string iotHubConnectionString, int partitionCount, string version)
        {
            this.IotHubConnectionString = iotHubConnectionString;
            this.PartitionCount = partitionCount;
            this.Version = version;
        }

        public string IotHubConnectionString { get; private set; }

        public int PartitionCount { get; private set; }

        public string Version { get; private set; }
    }
}
