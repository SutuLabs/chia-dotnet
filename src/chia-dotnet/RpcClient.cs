﻿using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Net.Security;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;

namespace chia.dotnet
{
    public class RpcClient : IDisposable
    {
        private readonly ClientWebSocket _webSocket = new();
        private readonly CancellationTokenSource _receiveCancellationTokenSource = new();
        private readonly ConcurrentDictionary<string, Message> _pendingMessages = new();
        private readonly ConcurrentDictionary<string, Message> _pendingResponses = new();
        private readonly EndpointInfo _endpoint;

        private bool disposedValue;

        public RpcClient(EndpointInfo endpoint)
        {
            _endpoint = endpoint;
            _webSocket.Options.RemoteCertificateValidationCallback += ValidateServerCertificate;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                var cert = CertLoader.GetCert(_endpoint.CertPath, _endpoint.KeyPath);
                _webSocket.Options.ClientCertificates = new X509Certificate2Collection(cert);

                await _webSocket.ConnectAsync(_endpoint.Uri, cancellationToken);
                _ = Task.Factory.StartNew(ReceiveLoop, _receiveCancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (Exception e)
            {
                e.Dump();
                throw;
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            _receiveCancellationTokenSource.Cancel();
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken);
        }

        public async Task PostMessage(Message message, CancellationToken cancellationToken)
        {
            var json = message.ToJson();
            await _webSocket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);
        }

        public async Task<Message> SendMessage(Message message, CancellationToken cancellationToken)
        {
            // capture the message to be sent
            if (!_pendingMessages.TryAdd(message.Request_Id, message))
            {
                throw new InvalidOperationException($"Message with id of {message.Request_Id} has already been sent");
            }

            try
            {
                var json = message.ToJson();
                await _webSocket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken);
            }
            catch
            {
                _pendingMessages.TryRemove(message.Request_Id, out _);
                throw;
            }

            // wait here until a response shows up or we get cancelled
            Message response;
            while (!_pendingResponses.TryRemove(message.Request_Id, out response) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Yield();
            }

            // the receive loop cleans up but make sure we do so on cancellation too
            if (_pendingMessages.ContainsKey(message.Request_Id))
            {
                _pendingMessages.TryRemove(message.Request_Id, out _);
            }

            return response;
        }

        private async Task ReceiveLoop()
        {
            var buffer = new ArraySegment<byte>(new byte[2048]);
            do
            {
                using var ms = new MemoryStream();

                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(buffer, _receiveCancellationTokenSource.Token);
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                ms.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var response = await reader.ReadToEndAsync();
                var message = Message.FromJson(response);

                // if we have a message pending with this id capture the response and remove it from the pending dictionary                
                if (_pendingMessages.TryRemove(message.Request_Id, out _))
                {
                    _pendingResponses[message.Request_Id] = message;
                }
                // TODO - broadcast any response received that's not in the pending dictionary

            } while (!_receiveCancellationTokenSource.IsCancellationRequested);
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // uncomment these checks to change remote cert validaiton requirements

            // require remote ca to be trusted on this machine
            //if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.RemoteCertificateChainErrors) 
            //    return false;

            // require server name to be validated in the cert
            //if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) == SslPolicyErrors.RemoteCertificateNameMismatch)
            //    return false;

            if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) == SslPolicyErrors.RemoteCertificateNotAvailable)
                return false;

            return true;
        }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _receiveCancellationTokenSource.Cancel();
                    _webSocket.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}