using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Iot.Tenant.WebService.ViewModels
{
    public class DeviceViewModel
    {
        public string Id { get; private set; }

        public DateTimeOffset Timestamp { get; private set; }

        public DeviceViewModel(string id, DateTimeOffset timestamp)
        {
            this.Id = id;
            this.Timestamp = timestamp;
        }
    }
}
