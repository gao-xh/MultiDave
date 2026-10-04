using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Protocol;

namespace DaveCoop.Core.Transport
{
    // Owns one stream. Serial writes prevent interleaving frames from concurrent callers.
    public sealed class FramedConnection : IDisposable
    {
        private readonly Stream _stream;
        private readonly SemaphoreSlim _write = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _read = new SemaphoreSlim(1, 1);
        private long _sent;
        private long _received;
        private int _disposed;

        public FramedConnection(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public async Task SendAsync(WirePacket packet, CancellationToken cancellation)
        {
            CheckOpen();
            await _write.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                CheckOpen();
                if (packet == null) throw new ArgumentNullException(nameof(packet));
                packet.Sequence = _sent + 1;
                try { await PacketFraming.WriteAsync(_stream, packet, cancellation).ConfigureAwait(false); }
                catch (ProtocolException) { throw; } // Validation failed before any bytes were written.
                catch { Dispose(); throw; }
                _sent = packet.Sequence;
            }
            finally { _write.Release(); }
        }

        public async Task<WirePacket> ReceiveAsync(CancellationToken cancellation)
        {
            CheckOpen();
            await _read.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                WirePacket packet = await PacketFraming.ReadAsync(_stream, cancellation).ConfigureAwait(false);
                if (packet.Sequence != _received + 1) throw new ProtocolException("Packet sequence gap or replay.");
                _received = packet.Sequence;
                return packet;
            }
            catch { Dispose(); throw; }
            finally { _read.Release(); }
        }

        private void CheckOpen()
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(FramedConnection));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) _stream.Dispose();
            // Waiters may still release the gates while disposal interrupts their I/O.
        }
    }
}
