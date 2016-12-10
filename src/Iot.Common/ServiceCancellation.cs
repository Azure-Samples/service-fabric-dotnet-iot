// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Common
{
    using System.Threading;

    public class ServiceCancellation
    {
        private readonly CancellationToken token;

        public ServiceCancellation(CancellationToken token)
        {
            this.token = token;
        }
        
        public CancellationToken Token
        {
            get
            {
                return this.token;
            }
        }
    }
}
