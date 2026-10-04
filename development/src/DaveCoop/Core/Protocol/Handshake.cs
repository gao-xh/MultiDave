using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Transport;

namespace DaveCoop.Core.Protocol
{
    public sealed class HandshakeResult
    {
        public string RoomId { get; set; }
        public int LocalPlayerId { get; set; }
        public int RemotePlayerId { get; set; }
        public PeerIdentity Peer { get; set; }
    }

    public static class Handshake
    {
        public static async Task<HandshakeResult> AcceptAsync(FramedConnection connection,
            PeerIdentity local, string roomId, CancellationToken cancellation)
        {
            PeerIdentity localCopy = PacketCodec.CopyIdentity(local);
            WirePacket hello = await connection.ReceiveAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (hello.Kind != PacketKind.Hello) throw new ProtocolException("Expected hello first.");
                PacketCodec.RequireCompatible(localCopy, hello.Hello);
            }
            catch (ProtocolException error)
            {
                await connection.SendAsync(new WirePacket { Kind = PacketKind.Reject, Reason = error.Message }, cancellation).ConfigureAwait(false);
                throw;
            }
            await connection.SendAsync(new WirePacket
            {
                Kind = PacketKind.Welcome, RoomId = roomId,
                Welcome = new Welcome { Identity = localCopy, RoomId = roomId }
            }, cancellation).ConfigureAwait(false);
            return new HandshakeResult { RoomId = roomId, LocalPlayerId = 1, RemotePlayerId = 2, Peer = PacketCodec.CopyIdentity(hello.Hello) };
        }

        public static async Task<HandshakeResult> JoinAsync(FramedConnection connection,
            PeerIdentity local, CancellationToken cancellation)
        {
            PeerIdentity localCopy = PacketCodec.CopyIdentity(local);
            await connection.SendAsync(new WirePacket { Kind = PacketKind.Hello, Hello = localCopy }, cancellation).ConfigureAwait(false);
            WirePacket response = await connection.ReceiveAsync(cancellation).ConfigureAwait(false);
            if (response.Kind == PacketKind.Reject) throw new ProtocolException("Peer rejected connection: " + response.Reason);
            if (response.Kind != PacketKind.Welcome) throw new ProtocolException("Expected welcome.");
            PacketCodec.RequireCompatible(localCopy, response.Welcome.Identity);
            return new HandshakeResult
            {
                RoomId = response.Welcome.RoomId, LocalPlayerId = response.Welcome.AssignedPlayerId,
                RemotePlayerId = response.Welcome.HostPlayerId, Peer = PacketCodec.CopyIdentity(response.Welcome.Identity)
            };
        }
    }
}
