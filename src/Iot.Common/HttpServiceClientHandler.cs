// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Iot.Common
{
    using Microsoft.ServiceFabric.Services.Client;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Fabric;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    public class HttpServiceClientHandler : HttpClientHandler
    {
        private const int MaxRetries = 5;
        private const int InitialRetryDelayMs = 25;
        private readonly Random random = new Random();

        public HttpServiceClientHandler()
        { }

        /// <summary>
        /// http://fabric/app/service/#/partitionkey/any|primary|secondary/endpoint-name/api-path
        /// </summary>
        /// <param name="request"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ServicePartitionResolver resolver = ServicePartitionResolver.GetDefault();
            ResolvedServicePartition partition = null;
            HttpServiceUriBuilder uriBuilder = new HttpServiceUriBuilder(request.RequestUri);

            int retries = MaxRetries;
            int retryDelay = InitialRetryDelayMs;
            bool resolveAddress = true;

            HttpResponseMessage lastResponse = null;
            Exception lastException = null;

            while (retries --> 0)
            {
                lastResponse = null;
                cancellationToken.ThrowIfCancellationRequested();

                if (resolveAddress)
                {
                    partition = partition != null
                        ? await resolver.ResolveAsync(partition, cancellationToken)
                        : await resolver.ResolveAsync(uriBuilder.ServiceName, uriBuilder.PartitionKey, cancellationToken);

                    string serviceEndpointJson;

                    switch (uriBuilder.Target)
                    {
                        case HttpServiceUriTarget.Primary:
                            serviceEndpointJson = partition.GetEndpoint().Address;
                            break;
                        case HttpServiceUriTarget.Secondary:
                            serviceEndpointJson = partition.Endpoints.ElementAt(this.random.Next(1, partition.Endpoints.Count)).Address;
                            break;
                        case HttpServiceUriTarget.Any:
                        case HttpServiceUriTarget.Default:
                        default:
                            serviceEndpointJson = partition.Endpoints.ElementAt(this.random.Next(0, partition.Endpoints.Count)).Address;
                            break;
                    }

                    string endpointUrl = JObject.Parse(serviceEndpointJson)["Endpoints"][uriBuilder.EndpointName].Value<string>();

                    request.RequestUri = new Uri($"{endpointUrl.TrimEnd('/')}/{uriBuilder.ServicePathAndQuery.TrimStart('/')}", UriKind.Absolute);
                }

                try
                {
                    lastResponse = await base.SendAsync(request, cancellationToken);

                    if (lastResponse.StatusCode == HttpStatusCode.NotFound ||
                        lastResponse.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        resolveAddress = true;
                    }
                    else
                    {
                        return lastResponse;
                    }
                }
                catch (TimeoutException te)
                {
                    lastException = te;
                    resolveAddress = true;
                }
                catch (SocketException se)
                {
                    lastException = se;
                    resolveAddress = true;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is WebException)
                {
                    lastException = ex;
                    WebException we = ex as WebException;

                    if (we == null)
                    {
                        we = ex.InnerException as WebException;
                    }

                    if (we != null)
                    {
                        HttpWebResponse errorResponse = we.Response as HttpWebResponse;

                        // the following assumes port sharing
                        // where a port is shared by multiple replicas within a host process using a single web host (e.g., http.sys).
                        if (we.Status == WebExceptionStatus.ProtocolError)
                        {
                            if (errorResponse.StatusCode == HttpStatusCode.NotFound ||
                                errorResponse.StatusCode == HttpStatusCode.ServiceUnavailable)
                            {
                                // This could either mean we requested an endpoint that does not exist in the service API (a user error)
                                // or the address that was resolved by fabric client is stale (transient runtime error) in which we should re-resolve.
                                resolveAddress = true;
                            }

                            if (errorResponse.StatusCode == HttpStatusCode.InternalServerError)
                            {
                                // The address is correct, but the server processing failed.
                                // This could be due to conflicts when writing the word to the dictionary.
                                // Retry the operation without re-resolving the address.
                                resolveAddress = false;
                            }
                        }

                        if (we.Status == WebExceptionStatus.Timeout ||
                            we.Status == WebExceptionStatus.RequestCanceled ||
                            we.Status == WebExceptionStatus.ConnectionClosed ||
                            we.Status == WebExceptionStatus.ConnectFailure)
                        {
                            resolveAddress = true;
                        }
                    }
                    else
                    {
                        resolveAddress = true;
                    }
                }

                await Task.Delay(retryDelay);

                retryDelay += retryDelay;
            }

            if (lastResponse != null)
            {
                return lastResponse;
            }
            else
            {
                throw lastException;
            }
        }

    }
}
