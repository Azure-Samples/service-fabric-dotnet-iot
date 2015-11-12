// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace IoTProcessorManagementService
{
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Web.Http;

    internal static class Utils
    {
        public static void ThrowHttpError(params string[] Errors)
        {
            ThrowHttpError(HttpStatusCode.BadRequest, Errors);
        }

        public static void ThrowHttpError(HttpStatusCode httpStatus, params string[] Errors)
        {
            HttpResponseMessage responseMessage = new HttpResponseMessage(HttpStatusCode.BadRequest);
            responseMessage.Content = new StringContent(Errors.ToMultiLine(), Encoding.UTF8);
            throw new HttpResponseException(responseMessage);
        }

        public static string ToMultiLine(this string[] array)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string e in array)
            {
                sb.AppendLine(e);
            }

            return sb.ToString();
        }
    }
}