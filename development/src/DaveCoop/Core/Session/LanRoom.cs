using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Transport;

namespace DaveCoop.Core.Session
{
    public sealed class LanHost : IDisposable
    {
        private readonly TcpListener _listener;
        private int _started;
        public int Port { get; }
        public string RoomId { get; } = Guid.NewGuid().ToString("N");

        public LanHost(IPAddress address, int port)
        {
            if (address == null || port < 0 || port > 65535) throw new ArgumentException("Invalid listen endpoint.");
            _listener = new TcpListener(address, port);
            _listener.Start(1);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public async Task<SessionPeer> AcceptOneAsync(PeerIdentity identity, CancellationToken cancellation, SessionOptions options = null)
        {
            PeerIdentity local = PacketCodec.CopyIdentity(identity);
            if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("This room has already accepted a peer.");
            TcpClient client = null;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellation).ConfigureAwait(false);
                client.NoDelay = true;
                // The framed connection owns the NetworkStream and its socket after handshake.
                return await SessionPeer.AcceptAsync(new FramedConnection(client.GetStream()), local,
                    RoomId, cancellation, options).ConfigureAwait(false);
            }
            catch { client?.Dispose(); throw; }
            finally { _listener.Stop(); }
        }

        public void Dispose() => _listener.Stop();
    }

    public static class LanGuest
    {
        public static async Task<SessionPeer> ConnectAsync(string address, int port, PeerIdentity identity,
            CancellationToken cancellation, SessionOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(address) || port < 1 || port > 65535) throw new ArgumentException("Invalid host endpoint.");
            PeerIdentity local = PacketCodec.CopyIdentity(identity);
            var client = new TcpClient { NoDelay = true };
            try
            {
                SessionOptions validated = (options ?? new SessionOptions()).CopyValidated();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(validated.HandshakeTimeoutSeconds));
                await client.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
                return await SessionPeer.JoinAsync(new FramedConnection(client.GetStream()), local,
                    cancellation, validated).ConfigureAwait(false);
            }
            catch { client.Dispose(); throw; }
        }
    }
}
