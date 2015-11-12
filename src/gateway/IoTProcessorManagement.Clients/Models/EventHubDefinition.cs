// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Clients
{
    public class EventHubDefinition
    {
        public string ConnectionString { get; set; } = string.Empty;

        public string EventHubName { get; set; } = string.Empty;

        public string ConsumerGroupName { get; set; } = string.Empty;
    }
}