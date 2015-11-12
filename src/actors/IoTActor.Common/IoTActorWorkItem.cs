// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTActor.Common
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Microsoft.WindowsAzure.Storage.Table;
    using Newtonsoft.Json.Linq;

    public class IoTActorWorkItem
    {
        public string DeviceId { get; set; } = string.Empty;

        public string EventHubName { get; set; } = string.Empty;

        public string ServiceBusNS { get; set; } = string.Empty;

        public byte[] Body { get; set; }

        public JObject toJObject()
        {
            JObject j = JObject.Parse(Encoding.UTF8.GetString(this.Body));
            j.Add("EventHubName", this.EventHubName);
            j.Add("ServiceBusNS", this.ServiceBusNS);

            return j;
        }

        public DynamicTableEntity ToDynamicTableEntity()
        {
            DynamicTableEntity entity = new DynamicTableEntity();
            JObject j = this.toJObject();

            entity.PartitionKey = string.Format(
                "{0}-{1}-{2}",
                j.Value<string>("DeviceId"),
                j.Value<string>("EventHubName"),
                j.Value<string>("ServiceBusNS")
                );
            entity.RowKey = DateTime.UtcNow.Ticks.ToString();
            foreach (KeyValuePair<string, JToken> t in j)
            {
                entity.Properties.Add(t.Key, new EntityProperty(t.Value.ToString()));
            }


            return entity;
        }
    }
}