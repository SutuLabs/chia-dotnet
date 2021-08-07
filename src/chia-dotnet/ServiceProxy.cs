﻿using System;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace chia.dotnet
{
    /// <summary>
    /// Base class that uses an <see cref="IRpcClient"/> to send and receive messages to other services
    /// </summary>
    /// <remarks>The lifetime of the RpcClient is not controlled by the proxy. It should be disposed outside of this class.</remarks>
    public abstract class ServiceProxy
    {
        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="rpcClient"><see cref="IRpcClient"/> instance to use for rpc communication</param>
        /// <param name="destinationService"><see cref="Message.Destination"/></param>
        /// <param name="originService"><see cref="Message.Origin"/></param>        
        public ServiceProxy(IRpcClient rpcClient, string destinationService, string originService)
        {
            RpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));

            if (string.IsNullOrEmpty(destinationService))
            {
                throw new ArgumentNullException(nameof(destinationService));
            }

            if (string.IsNullOrEmpty(originService))
            {
                throw new ArgumentNullException(nameof(originService));
            }

            DestinationService = destinationService;
            OriginService = originService;
        }

        /// <summary>
        /// The name of the service that is running. Will be used as the <see cref="Message.Origin"/> of all messages
        /// as well as the identifier used for <see cref="DaemonProxy.RegisterService(string, CancellationToken)"/>
        /// </summary>
        public string OriginService { get; init; }

        /// <summary>
        /// <see cref="Message.Destination"/>
        /// </summary>
        public string DestinationService { get; init; }

        /// <summary>
        /// The <see cref="IRpcClient"/> used for underlying RPC
        /// </summary>
        public IRpcClient RpcClient { get; init; }

        /// <summary>
        /// Sends ping message to the service
        /// </summary>
        /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
        /// <returns>Awaitable <see cref="Task"/></returns>
        public async Task Ping(CancellationToken cancellationToken = default)
        {
            _ = await SendMessage("ping", cancellationToken);
        }

        /// <summary>
        /// Stops the node
        /// </summary>
        /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
        /// <returns>Awaitable <see cref="Task"/></returns>
        public async Task StopNode(CancellationToken cancellationToken = default)
        {
            _ = await SendMessage("stop_node", cancellationToken);
        }

        /// <summary>
        /// Get connections that the service has
        /// </summary>
        /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
        /// <returns>A list of connections</returns>
        public async Task<IEnumerable<dynamic>> GetConnections(CancellationToken cancellationToken = default)
        {
            var response = await SendMessage("get_connections", cancellationToken);

            return response.connections;
        }

        /// <summary>
        /// Add a connection
        /// </summary>
        /// <param name="host">The host name of the connection</param>
        /// <param name="port">The port to use</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
        /// <returns>An awaitable <see cref="Task"/></returns>
        public async Task OpenConnection(string host, int port, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new ArgumentNullException(nameof(host));
            }

            dynamic data = new ExpandoObject();
            data.host = host;
            data.port = port;

            _ = await SendMessage("open_connection", data, cancellationToken);
        }

        /// <summary>
        /// Closes a connection
        /// </summary>
        /// <param name="nodeId">The id of the node to close</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
        /// <returns>An awaitable <see cref="Task"/></returns>
        public async Task CloseConnection(string nodeId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                throw new ArgumentNullException(nameof(nodeId));
            }

            dynamic data = new ExpandoObject();
            data.node_id = nodeId;

            _ = await SendMessage("close_connection", data, cancellationToken);
        }

        internal async Task<dynamic> SendMessage(string command, dynamic data = null, CancellationToken cancellationToken = default)
        {
            var message = Message.Create(command, data, DestinationService, OriginService);
            return await RpcClient.SendMessage(message, cancellationToken);
        }

        internal async Task<T> SendMessage<T>(string command, dynamic data, string item = null, CancellationToken cancellationToken = default) where T : new()
        {
            var d = await SendMessage(command, data, cancellationToken);

            return Convert<T>(d, item);
        }

        internal async Task<IEnumerable<T>> SendMessageCollection<T>(string command, dynamic data, string item = null, CancellationToken cancellationToken = default) where T : new()
        {
            var d = await SendMessage(command, data, cancellationToken);

            return Convert<List<T>>(d, item);
        }

        private static T Convert<T>(dynamic o, string item)
        {
            var j = o as JObject;
            var token = string.IsNullOrEmpty(item) ? j : j.GetValue(item);

            var serializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                }
            };

            var serializer = JsonSerializer.Create(serializerSettings);
            return token.ToObject<T>(serializer);
        }
    }
}
