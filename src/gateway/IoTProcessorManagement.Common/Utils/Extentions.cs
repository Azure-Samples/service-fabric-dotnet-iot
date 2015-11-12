// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagement.Common
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    public static class Extentions
    {
        public static Task<Byte[]> ToBytes(this Stream stream)
        {
            byte[] bytes = new byte[stream.Length];
            int read, current = 0;
            while ((read = stream.Read(bytes, current, bytes.Length - current)) > 0)
            {
                current += read;
            }

            return Task.FromResult(bytes);
        }

        public static string GetCombinedExceptionMessage(this AggregateException ae)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Exception e in ae.InnerExceptions)
            {
                sb.AppendLine(string.Concat("E: ", e.Message));
            }

            return sb.ToString();
        }

        public static string GetCombinedExceptionStackTrace(this AggregateException ae)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Exception e in ae.InnerExceptions)
            {
                sb.AppendLine(string.Concat("StackTrace: ", e.StackTrace));
            }

            return sb.ToString();
        }
    }
}