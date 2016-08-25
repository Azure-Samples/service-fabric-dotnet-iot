// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Common
{
    using Microsoft.ServiceFabric.Services.Client;
    using System;
    using System.Fabric;
    using System.Linq;

    /// <summary>
    /// http://fabric/app/service/#/partitionkey/any|primary|secondary/endpoint-name/api-path
    /// </summary>
    public class HttpServiceUriBuilder
    {
        private const short FabricSchemeLength = 8;

        public string Scheme { get; private set; }

        public string Host { get; private set; }

        public Uri ServiceName { get; private set; }

        public ServicePartitionKey PartitionKey { get; private set; }

        public HttpServiceUriTarget Target { get; private set; }

        public string EndpointName { get; private set; }

        public string ServicePathAndQuery { get; private set; }

        public HttpServiceUriBuilder()
        {
            this.Scheme = "http";
            this.Host = "fabric";
            this.Target = HttpServiceUriTarget.Default;
        }

        public HttpServiceUriBuilder(string uri)
            : this (new Uri(uri, UriKind.Absolute))
        {
        }

        public HttpServiceUriBuilder(Uri uri)
        {
            this.Scheme = uri.Scheme;
            this.Host = uri.Host;
            this.ServiceName = new Uri("fabric:" + uri.AbsolutePath.TrimEnd('/'));

            string path = uri.Fragment.Remove(0, 2);
            string[] segments = path.Split('/');

            long int64PartitionKey;
            this.PartitionKey = Int64.TryParse(segments[0], out int64PartitionKey)
                ? new ServicePartitionKey(int64PartitionKey)
                : String.IsNullOrEmpty(segments[0])
                    ? new ServicePartitionKey() 
                    : new ServicePartitionKey(segments[0]);

            HttpServiceUriTarget target;
            if (!Enum.TryParse<HttpServiceUriTarget>(segments[1], true, out target))
            {
                throw new ArgumentException();
            }

            this.Target = target;
            this.EndpointName = segments[2];
            this.ServicePathAndQuery = String.Join("/", segments.Skip(3));
        }

        public override string ToString()
        {
            return base.ToString();
        }

        public Uri Build()
        {
            if (this.ServiceName == null)
            {
                throw new UriFormatException("Service name is null.");
            }

            UriBuilder builder = new UriBuilder();
            builder.Scheme = String.IsNullOrEmpty(this.Scheme) ? "http" : this.Scheme;
            builder.Host = String.IsNullOrEmpty(this.Host) ? "fabric" : this.Host;
            builder.Path = this.ServiceName.AbsolutePath.Trim('/') + '/';
;
            string partitionKey = this.PartitionKey == null || this.PartitionKey.Kind == ServicePartitionKind.Singleton
                ? String.Empty
                : this.PartitionKey.Value.ToString();

            builder.Fragment = $"/{partitionKey}/{this.Target.ToString()}/{this.EndpointName ?? String.Empty}/{this.ServicePathAndQuery ?? String.Empty}";

            return builder.Uri;
        }

        public HttpServiceUriBuilder SetHost(string host)
        {
            this.Host = host?.ToLowerInvariant();
            return this;
        }

        public HttpServiceUriBuilder SetScheme(string scheme)
        {
            this.Scheme = scheme?.ToLowerInvariant();
            return this;
        }

        /// <summary>
        /// Fully-qualified service name URI: fabric:/name/of/service
        /// </summary>
        /// <param name="serviceName"></param>
        /// <returns></returns>
        public HttpServiceUriBuilder SetServiceName(Uri serviceName)
        {
            if (serviceName != null)
            {
                if (!serviceName.IsAbsoluteUri)
                {
                    throw new UriFormatException("Service URI must be an absolute URI in the form 'fabric:/name/of/service");
                }

                if (!String.Equals(serviceName.Scheme, "fabric", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UriFormatException("Scheme must be 'fabric'.");
                }
            }

            this.ServiceName = serviceName;

            return this;
        }
        
        /// <summary>
        /// Fully-qualified service name URI: fabric:/name/of/service
        /// </summary>
        /// <param name="serviceName"></param>
        /// <returns></returns>
        public HttpServiceUriBuilder SetServiceName(string serviceName)
        {
            return this.SetServiceName(new Uri(serviceName, UriKind.Absolute));
        }

        public HttpServiceUriBuilder SetPartitionKey(string namedPartitionKey)
        {
            this.PartitionKey = new ServicePartitionKey(namedPartitionKey);
            return this;
        }

        public HttpServiceUriBuilder SetPartitionKey(long int64PartitionKey)
        {
            this.PartitionKey = new ServicePartitionKey(int64PartitionKey);
            return this;
        }

        public HttpServiceUriBuilder SetTarget(HttpServiceUriTarget target)
        {
            this.Target = target;
            return this;
        }

        public HttpServiceUriBuilder SetEndpointName(string endpointName)
        {
            this.EndpointName = endpointName;
            return this;
        }

        public HttpServiceUriBuilder SetServicePathAndQuery(string servicePathAndQuery)
        {
            this.ServicePathAndQuery = servicePathAndQuery;
            return this;
        }
    }
}
