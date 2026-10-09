//
// StreamEmber Live: shared between gtav-runtime-scripthook and rdr2-runtime-scripthook (keep identical).
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StreamEmber.Live.Internal
{
    /// <summary>
    /// EventFabric realtime connection: <c>/events/ws?format=json&amp;customerUuid=…&amp;applicationUuid=…</c>.
    /// Reading needs no token; a valid runtime token is attached (Bearer) when one is at hand.
    /// Only <c>packetType: action</c> frames are forwarded. The last cursor is kept and sent as
    /// <c>replay=true&amp;cursor=…</c> after a disconnect. Reconnect after 1, 2, 5, 10, 20, 30 s; ping every 20 s.
    /// Runs on background threads; <c>onAction</c> is called there (the service queues it for the script thread).
    /// </summary>
    internal sealed class EventFabricClient : IDisposable
    {
        private static readonly int[] BackoffSeconds = { 1, 2, 5, 10, 20, 30 };
        private const int MaxFrameBytes = 4 * 1024 * 1024;

        private readonly string _wsBaseUrl;
        private readonly string _customerUuid;
        private readonly string _applicationUuid;
        private readonly Func<RuntimeToken> _bearer;
        private readonly Action<Dictionary<string, object>> _onAction;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private volatile ClientWebSocket _socket;
        private volatile string _cursor;
        private int _attempts;
        private bool _everConnected;

        public EventFabricClient(string wsBaseUrl, string customerUuid, string applicationUuid,
                                 Func<RuntimeToken> bearer, Action<Dictionary<string, object>> onAction)
        {
            _wsBaseUrl = wsBaseUrl;
            _customerUuid = customerUuid;
            _applicationUuid = applicationUuid;
            _bearer = bearer;
            _onAction = onAction;
        }

        public string CustomerUuid => _customerUuid;

        public bool IsConnected => _socket?.State == WebSocketState.Open;

        /// <summary>true = connected, false = lost. Raised on a background thread.</summary>
        public event Action<bool> ConnectionChanged;

        public void Start()
        {
            Task.Run(RunAsync);
            Task.Run(PingLoopAsync);
        }

        public void Dispose()
        {
            if (_cts.IsCancellationRequested) return;
            _cts.Cancel();
            ClientWebSocket socket = _socket;
            _socket = null;
            if (socket == null) return;
            try
            {
                if (socket.State == WebSocketState.Open)
                    socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(1000);
            }
            catch (Exception)
            {
                // closing anyway
            }
            socket.Dispose();
        }

        private async Task RunAsync()
        {
            CancellationToken token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                try
                {
                    // A rejected token must never block reading: every other attempt goes without it.
                    RuntimeToken bearer = _attempts % 2 == 0 ? _bearer?.Invoke() : null;
                    if (bearer != null) socket.Options.SetRequestHeader("Authorization", "Bearer " + bearer.Value);
                    Uri uri = BuildUri();
                    LiveLog.Debug("Connecting to " + uri.GetLeftPart(UriPartial.Path) + (bearer != null ? " (with runtime token)" : string.Empty));
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
                    }
                    _socket = socket;
                    _attempts = 0;
                    LiveLog.Info((_everConnected ? "Reconnected" : "Connected") + " to EventFabric (application " + _applicationUuid + ").");
                    _everConnected = true;
                    ConnectionChanged?.Invoke(true);
                    await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
                    if (!token.IsCancellationRequested) LiveLog.Warn("EventFabric connection closed; reconnecting.");
                }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    int failures = ++_attempts;
                    if (failures <= 3 || failures % 10 == 0 || LiveLog.DebugEnabled)
                        LiveLog.Warn("EventFabric connection failed (attempt " + failures + "): " + Http.Describe(error));
                }
                catch (Exception)
                {
                    // cancelled
                }
                finally
                {
                    if (_socket == socket)
                    {
                        _socket = null;
                        ConnectionChanged?.Invoke(false);
                    }
                    socket.Dispose();
                }

                if (token.IsCancellationRequested) break;
                int delay = BackoffSeconds[Math.Min(Math.Max(_attempts - 1, 0), BackoffSeconds.Length - 1)];
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private Uri BuildUri()
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("format", "json"),
                new KeyValuePair<string, string>("customerUuid", _customerUuid),
                new KeyValuePair<string, string>("applicationUuid", _applicationUuid)
            };
            string resumeFrom = _cursor;
            if (!string.IsNullOrWhiteSpace(resumeFrom))
            {
                query.Add(new KeyValuePair<string, string>("replay", "true"));
                query.Add(new KeyValuePair<string, string>("cursor", resumeFrom));
            }
            return new Uri(_wsBaseUrl + "/events/ws?" + Http.Query(query));
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            using (var message = new MemoryStream())
            {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxFrameBytes) throw new InvalidDataException("EventFabric frame is too large.");
                    if (!result.EndOfMessage) continue;
                    string text = result.MessageType == WebSocketMessageType.Text
                        ? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)
                        : null;
                    message.SetLength(0);
                    if (text == null) continue;
                    try
                    {
                        Handle(text);
                    }
                    catch (Exception error)
                    {
                        LiveLog.Warn("EventFabric frame could not be handled: " + error.Message);
                    }
                }
            }
        }

        private void Handle(string text)
        {
            if (!(Json.Parse(text) is Dictionary<string, object> frame)) return;
            string frameCursor = Json.Text(frame, "cursor");
            if (frameCursor.Length > 0) _cursor = frameCursor;

            switch (Json.Text(frame, "packetType"))
            {
                case "action":
                    Dictionary<string, object> action = Json.Obj(frame, "action");
                    if (action != null) _onAction(action);
                    break;
                case "system":
                    Dictionary<string, object> system = Json.Obj(frame, "system");
                    if (system != null) HandleSystem(system);
                    break;
                // packetType=event: raw events are not needed in game (GCore turns them into actions).
            }
        }

        private void HandleSystem(Dictionary<string, object> system)
        {
            string type = Json.Text(system, "type");
            LiveLog.Debug("system: " + Json.Write(system));
            if (Json.Bool(system, "cursorExpired"))
            {
                string reason = Json.Text(system, "cursorExpiredReason");
                if (reason == "boot_changed") LiveLog.Info("EventFabric was restarted; live actions resumed.");
                else LiveLog.Warn("Replay window passed (" + reason + "); actions sent while disconnected may have been missed.");
            }
            if (type == "replay.completed" && Json.Bool(system, "hasMore"))
            {
                string next = _cursor;
                if (!string.IsNullOrWhiteSpace(next))
                {
                    var command = Json.NewObject();
                    command["type"] = "replay.next";
                    command["cursor"] = next;
                    command["limit"] = 100;
                    _ = SendAsync(Json.Write(command));
                }
            }
            else if (type == "command.rejected")
            {
                LiveLog.Warn("EventFabric rejected a command: " + Json.Text(system, "message"));
            }
        }

        private async Task PingLoopAsync()
        {
            CancellationToken token = _cts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                await SendAsync("{\"type\":\"ping\"}").ConfigureAwait(false);
            }
        }

        private async Task SendAsync(string text)
        {
            ClientWebSocket socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open) return;
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                LiveLog.Debug("Send failed: " + error.Message);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
