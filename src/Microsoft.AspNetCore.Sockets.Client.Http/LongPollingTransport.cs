// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Sockets.Client.Http;
using Microsoft.AspNetCore.Protocols.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Protocols;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public partial class LongPollingTransport : ITransport
    {
        private readonly HttpClient _httpClient;
        private readonly HttpOptions _httpOptions;
        private readonly ILogger _logger;
        private IDuplexPipe _application;
        private Task _sender;
        private Task _poller;
        private HttpResponseMessage _response;

        private readonly CancellationTokenSource _transportCts = new CancellationTokenSource();

        public Task Running { get; private set; } = Task.CompletedTask;

        public LongPollingTransport(HttpClient httpClient)
            : this(httpClient, null, null)
        { }

        public LongPollingTransport(HttpClient httpClient, HttpOptions httpOptions, ILoggerFactory loggerFactory)
        {
            _httpClient = httpClient;
            _httpOptions = httpOptions;
            _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LongPollingTransport>();
        }

        public Task StartAsync(Uri url, IDuplexPipe application, TransferFormat transferFormat, IConnection connection)
        {
            if (transferFormat != TransferFormat.Binary && transferFormat != TransferFormat.Text)
            {
                throw new ArgumentException($"The '{transferFormat}' transfer format is not supported by this transport.", nameof(transferFormat));
            }

            connection.Features.Set<IConnectionInherentKeepAliveFeature>(new ConnectionInherentKeepAliveFeature(_httpClient.Timeout));

            _application = application;

            Log.StartTransport(_logger, transferFormat);

            // Start sending and polling (ask for binary if the server supports it)
            var startTcs = new TaskCompletionSource<object>();
            _poller = Poll(url, startTcs, _transportCts.Token);
            _sender = SendUtils.SendMessages(url, _application, _httpClient, _httpOptions, _transportCts, _logger);

            Running = Task.WhenAll(_sender, _poller).ContinueWith(t =>
            {
                Log.TransportStopped(_logger, t.Exception?.InnerException);
                _application.Output.Complete(t.Exception?.InnerException);
                _application.Input.Complete();
                return t;
            }).Unwrap();

            return startTcs.Task;
        }

        public async Task StopAsync()
        {
            Log.TransportStopping(_logger);

            _transportCts.Cancel();

            try
            {
                _response?.Dispose();
                await Running;
            }
            catch
            {
                // exceptions have been handled in the Running task continuation by closing the channel with the exception
            }
        }

        private async Task Poll(Uri pollUrl, TaskCompletionSource<object> startTcs,CancellationToken cancellationToken)
        {
            Log.StartReceive(_logger);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, pollUrl);
                    SendUtils.PrepareHttpRequest(request, _httpOptions);

                    try
                    {
                        _response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // SendAsync will throw the OperationCanceledException if the passed cancellationToken is canceled
                        // or if the http request times out due to HttpClient.Timeout expiring. In the latter case we
                        // just want to start a new poll.
                        continue;
                    }

                    _response.EnsureSuccessStatusCode();
                    startTcs.TrySetResult(null);

                    if (_response.StatusCode == HttpStatusCode.NoContent || cancellationToken.IsCancellationRequested)
                    {
                        Log.ClosingConnection(_logger);

                        // Transport closed or polling stopped, we're done
                        break;
                    }
                    else
                    {
                        Log.ReceivedMessages(_logger);

                        var stream = new PipeWriterStream(_application.Output);
                        try
                        {
                            await _response.Content.CopyToAsync(stream);
                        }
                        catch { /*First request*/};
                        await _application.Output.FlushAsync();
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                // transport is being closed
                startTcs.TrySetException(ex);
                Log.ReceiveCanceled(_logger);
            }
            catch (Exception ex)
            {
                startTcs.TrySetException(ex);
                Log.ErrorPolling(_logger, pollUrl, ex);
                throw;
            }
            finally
            {
                // Make sure the send loop is terminated
                _transportCts.Cancel();
                Log.ReceiveStopped(_logger);
            }
        }
    }
}
