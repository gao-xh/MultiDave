using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.Transport
{
    public static class PacketFraming
    {
        public static async Task WriteAsync(Stream stream, WirePacket packet, CancellationToken cancellation)
        {
            byte[] body = PacketCodec.Encode(packet);
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
            await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellation).ConfigureAwait(false);
        }

        public static async Task<WirePacket> ReadAsync(Stream stream, CancellationToken cancellation)
        {
            byte[] header = new byte[4];
            await ReadExactAsync(stream, header, cancellation).ConfigureAwait(false);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length < 1 || length > PacketCodec.MaxPacketBytes) throw new ProtocolException("Invalid packet length prefix.");
            byte[] body = new byte[length];
            await ReadExactAsync(stream, body, cancellation).ConfigureAwait(false);
            return PacketCodec.Decode(body);
        }

        private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellation)
        {
            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.Slice(filled), cancellation).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Peer closed the packet stream.");
                filled += read;
            }
        }
    }
}
