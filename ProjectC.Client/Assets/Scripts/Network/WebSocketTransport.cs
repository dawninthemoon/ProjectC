using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProjectC.Client
{
    // No Unity dependencies. Background tasks communicate exclusively through queues.
    public sealed class WebSocketTransport : IDisposable
    {
        public readonly ConcurrentQueue<string> Messages = new ConcurrentQueue<string>();
        public readonly ConcurrentQueue<string> Notices = new ConcurrentQueue<string>();
        public volatile bool Connected;
        readonly ClientWebSocket socket = new ClientWebSocket();
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        readonly SemaphoreSlim sender = new SemaphoreSlim(1, 1);

        public Task ConnectAsync(string address)
        {
            return Task.Run(async () =>
            {
                try
                {
                    var uri = new Uri(address);
                    if (uri.Scheme != "ws" && uri.Scheme != "wss") throw new ArgumentException("Use ws:// or wss://");
                    await socket.ConnectAsync(uri, cancellation.Token).ConfigureAwait(false);
                    Connected = true;
                    Notices.Enqueue("Connected");
                    var buffer = new byte[8192];
                    while (!cancellation.IsCancellationRequested)
                    {
                        using (var message = new MemoryStream())
                        {
                            WebSocketReceiveResult result;
                            do
                            {
                                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation.Token).ConfigureAwait(false);
                                if (result.MessageType == WebSocketMessageType.Close) return;
                                if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 1048576)
                                    throw new IOException("Invalid server message");
                                message.Write(buffer, 0, result.Count);
                            } while (!result.EndOfMessage);
                            if (Messages.Count >= 256) throw new IOException("Receive queue full; reconnect to resync");
                            Messages.Enqueue(new UTF8Encoding(false, true).GetString(message.ToArray()));
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { Notices.Enqueue(error.GetType().Name + ": " + error.Message); }
                finally { Connected = false; socket.Dispose(); Notices.Enqueue("Disconnected"); }
            });
        }

        public async Task SendAsync(string json)
        {
            bool held = false;
            try
            {
                await sender.WaitAsync(cancellation.Token).ConfigureAwait(false);
                held = true;
                if (!Connected) throw new IOException("Not connected");
                var bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Notices.Enqueue("Send failed: " + error.Message); }
            finally { if (held) sender.Release(); }
        }
        public void Dispose()
        {
            Connected = false;
            cancellation.Cancel();
            socket.Abort();
            // Keep synchronization primitives alive until outstanding async operations complete.
        }
    }
}
