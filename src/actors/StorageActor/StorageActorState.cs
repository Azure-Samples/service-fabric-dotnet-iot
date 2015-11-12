// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace StorageActor
{
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using IoTActor.Common;

    [DataContract]
    public class StorageActorState
    {
        [DataMember] public Queue<IoTActorWorkItem> Queue = new Queue<IoTActorWorkItem>();
    }
}