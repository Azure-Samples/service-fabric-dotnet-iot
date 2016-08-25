// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Common
{
    public sealed class FnvHash
    {
        private const ulong FnvPrime = 1099511628211;
        private const ulong FnvOffsetBasis = 14695981039346656037;

        public static ulong Hash(byte[] value)
        {
            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < value.Length; ++i)
            {
                hash ^= value[i];
                hash *= FnvPrime;
            }

            return hash;
        }
    }
}