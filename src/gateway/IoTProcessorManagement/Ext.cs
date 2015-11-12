// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement
{
    using System;

    public static class Ext
    {
        public static void ThrowPowerShell(this AggregateException ae)
        {
            AggregateException aeEx = ae.Flatten();

            foreach (Exception e in aeEx.InnerExceptions)
            {
                Console.WriteLine("Exception:");
                Console.WriteLine(string.Format("Mesage:{0}", e.Message));
                Console.WriteLine(string.Format("StackTrace:{0}", e.StackTrace));
                Console.WriteLine("");
            }

            throw new Exception("One or more errors have occured, errors are above");
        }
    }
}