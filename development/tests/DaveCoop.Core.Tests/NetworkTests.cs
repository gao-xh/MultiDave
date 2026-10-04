using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Transport;

internal static class NetworkTests
{
    internal static void CodecRoundTrip()
    {
        WirePacket packet = FramePacket(Guid.NewGuid().ToString("N"), 1, 12.5);
        packet.Sequence = 1;
        WirePacket decoded = PacketCodec.Decode(PacketCodec.Encode(packet));
        Assert(decoded.Frame.Root.Position == packet.Frame.Root.Position, "position lost in JSON");
        Assert(decoded.Frame.Root.Rotation == packet.Frame.Root.Rotation, "quaternion fields lost in JSON");
        Assert(decoded.Frame.Root.Scale == Vector3.One && decoded.Frame.Parts[0].Color == Vector4.One, "numeric struct fields lost");
        Assert(decoded.Frame.Parts[0].SpriteKey == "fixture/sprite" && decoded.Frame.SceneEpoch == 1, "frame metadata lost");
        PacketCodec.RequireRoom(decoded, packet.RoomId);
        Throws<ProtocolException>(() => PacketCodec.RequireRoom(decoded, Guid.NewGuid().ToString("N")));
    }

    internal static void CodecRejectsInvalidData()
    {
        var packet = FramePacket(Guid.NewGuid().ToString("N"), 1, 1);
        packet.Sequence = 1;
        packet.Frame.Root = new Pose { Position = new Vector3(float.NaN, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One };
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        packet.Frame.Root = ValidPose();
        packet.Frame.Parts = new[] { packet.Frame.Parts[0], packet.Frame.Parts[0] };
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        packet.Frame.Parts = new SpritePartFrame[129];
        Throws<ProtocolException>(() => PacketCodec.Encode(packet));
        Throws<ProtocolException>(() => PacketCodec.Decode(Encoding.UTF8.GetBytes("{\"kind\":999,\"sequence\":1,\"reason\":\"bad\"}")));
        Throws<ProtocolException>(() => PacketCodec.Decode(Encoding.UTF8.GetBytes("null")));
        Throws<ProtocolException>(() => PacketCodec.Decode(Encoding.UTF8.GetBytes("{")));
        Throws<ProtocolException>(() => PacketCodec.Decode(new byte[PacketCodec.MaxPacketBytes + 1]));
    }

    internal static async Task FragmentedPackets()
    {
        var packet = FramePacket(Guid.NewGuid().ToString("N"), 1, 1);
        packet.Sequence = 1;
        using var output = new MemoryStream();
        await PacketFraming.WriteAsync(output, packet, CancellationToken.None);
        using var input = new ChunkedStream(output.ToArray());
        WirePacket result = await PacketFraming.ReadAsync(input, CancellationToken.None);
        Assert(result.Frame.PlayerId == 1 && result.Frame.Parts.Length == 1, "split packet corrupted");
        byte[] badHeader = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(badHeader, PacketCodec.MaxPacketBytes + 1);
        using var invalid = new MemoryStream(badHeader);
        await ThrowsAsync<ProtocolException>(() => PacketFraming.ReadAsync(invalid, CancellationToken.None));
        byte[] truncated = output.ToArray();
        Array.Resize(ref truncated, truncated.Length - 2);
        using var shortInput = new MemoryStream(truncated);
        await ThrowsAsync<EndOfStreamException>(() => PacketFraming.ReadAsync(shortInput, CancellationToken.None));
    }

    internal static async Task TcpHandshakeAndFrames()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await SocketPair.OpenAsync(timeout.Token);
        string room = Guid.NewGuid().ToString("N");
        Task<HandshakeResult> accepted = Handshake.AcceptAsync(pair.Server, Identity("Host"), room, timeout.Token);
        HandshakeResult joined = await Handshake.JoinAsync(pair.Client, Identity("Guest"), timeout.Token);
        HandshakeResult host = await accepted;
        Assert(host.LocalPlayerId == 1 && host.RemotePlayerId == 2 && joined.LocalPlayerId == 2 && joined.RoomId == room,
            "handshake assigned inconsistent identities");
        for (int i = 0; i < 20; i++)
        {
            await pair.Server.SendAsync(FramePacket(room, 1, i), timeout.Token);
            WirePacket frame = await pair.Client.ReceiveAsync(timeout.Token);
            PacketCodec.RequireRoom(frame, room);
            Assert(frame.Frame.PlayerId == 1 && frame.Frame.SampleTime == i, "host frame corrupted");
            await pair.Client.SendAsync(FramePacket(room, 2, i), timeout.Token);
            frame = await pair.Server.ReceiveAsync(timeout.Token);
            Assert(frame.Frame.PlayerId == 2 && frame.Frame.SampleTime == i, "guest frame corrupted");
        }
    }

    internal static async Task TcpRejectsIncompatibleBuild()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await SocketPair.OpenAsync(timeout.Token);
        PeerIdentity guest = Identity("Guest"); guest.SteamBuildId = "25315877";
        Task<HandshakeResult> accept = Handshake.AcceptAsync(pair.Server, Identity("Host"), Guid.NewGuid().ToString("N"), timeout.Token);
        await ThrowsAsync<ProtocolException>(() => Handshake.JoinAsync(pair.Client, guest, timeout.Token));
        await ThrowsAsync<ProtocolException>(() => accept);
    }

    internal static async Task ConcurrentWrites()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await SocketPair.OpenAsync(timeout.Token);
        string room = Guid.NewGuid().ToString("N");
        var writes = new Task[25];
        for (int i = 0; i < writes.Length; i++) writes[i] = pair.Client.SendAsync(new WirePacket
        {
            Kind = PacketKind.Ping, RoomId = room, Clock = new ClockMessage { Id = i + 1, Time = i }
        }, timeout.Token);
        var ids = new System.Collections.Generic.HashSet<long>();
        for (int i = 0; i < writes.Length; i++)
        {
            WirePacket packet = await pair.Server.ReceiveAsync(timeout.Token);
            Assert(packet.Sequence == i + 1 && ids.Add(packet.Clock.Id), "concurrent packets interleaved or duplicated");
        }
        await Task.WhenAll(writes);
        Assert(ids.Count == writes.Length, "concurrent packets missing");
    }

    internal static async Task InvalidSendDoesNotConsumeSequence()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await SocketPair.OpenAsync(timeout.Token);
        await ThrowsAsync<ProtocolException>(() => pair.Client.SendAsync(new WirePacket { Kind = PacketKind.Hello }, timeout.Token));
        await pair.Client.SendAsync(new WirePacket { Kind = PacketKind.Hello, Hello = Identity("Guest") }, timeout.Token);
        WirePacket packet = await pair.Server.ReceiveAsync(timeout.Token);
        Assert(packet.Sequence == 1 && packet.Kind == PacketKind.Hello, "failed validation poisoned next packet sequence");
    }

    internal static async Task ReplayAndDisconnect()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await SocketPair.OpenAsync(timeout.Token);
        var hello = new WirePacket { Kind = PacketKind.Hello, Sequence = 1, Hello = Identity("Guest") };
        await PacketFraming.WriteAsync(pair.ServerStream, hello, timeout.Token);
        await pair.Client.ReceiveAsync(timeout.Token);
        await PacketFraming.WriteAsync(pair.ServerStream, hello, timeout.Token);
        await ThrowsAsync<ProtocolException>(() => pair.Client.ReceiveAsync(timeout.Token));
        await ThrowsAsync<ObjectDisposedException>(() => pair.Client.SendAsync(hello, timeout.Token));
        await ThrowsAsync<EndOfStreamException>(() => pair.Server.ReceiveAsync(timeout.Token));
    }

    internal static async Task CancelBlockedRead()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pair = await SocketPair.OpenAsync(timeout.Token);
        using var cancelRead = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await ThrowsAsync<OperationCanceledException>(() => pair.Client.ReceiveAsync(cancelRead.Token));
        await ThrowsAsync<ObjectDisposedException>(() => pair.Client.SendAsync(new WirePacket { Kind = PacketKind.Hello, Hello = Identity("Guest") }, timeout.Token));
    }

    private static PeerIdentity Identity(string name) => new PeerIdentity
    {
        ModVersion = "core-tests", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name
    };

    private static Pose ValidPose() => new Pose
    {
        Position = new Vector3(10, 2, 3), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f), Scale = Vector3.One
    };

    private static WirePacket FramePacket(string room, int id, double time) => new WirePacket
    {
        Kind = PacketKind.PlayerFrame, RoomId = room,
        Frame = new PlayerFrame
        {
            PlayerId = id, SceneEpoch = 1, SceneKey = "fixture-scene", SampleTime = time, Root = ValidPose(),
            Parts = new[] { new SpritePartFrame { Slot = "body", SpriteKey = "fixture/sprite", Pose = ValidPose(), Color = Vector4.One, Visible = true } }
        }
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static async Task ThrowsAsync<T>(Func<Task> run) where T : Exception
    {
        try { await run(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class ChunkedStream : MemoryStream
    {
        public ChunkedStream(byte[] bytes) : base(bytes) { }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer.Slice(0, Math.Min(buffer.Length, 3)), cancellationToken);
    }

    private sealed class SocketPair : IDisposable
    {
        private TcpClient _client;
        private TcpClient _server;
        public FramedConnection Client;
        public FramedConnection Server;
        public NetworkStream ServerStream;

        public static async Task<SocketPair> OpenAsync(CancellationToken cancellation)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            TcpClient server = null;
            listener.Start();
            Task<TcpClient> accept = listener.AcceptTcpClientAsync(cancellation).AsTask();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                await client.ConnectAsync(IPAddress.Loopback, port, cancellation);
                server = await accept;
                server.NoDelay = true;
                NetworkStream serverStream = server.GetStream();
                return new SocketPair
                {
                    _client = client, _server = server, Client = new FramedConnection(client.GetStream()),
                    Server = new FramedConnection(serverStream), ServerStream = serverStream
                };
            }
            catch
            {
                client.Dispose(); server?.Dispose(); listener.Stop();
                try { (await accept).Dispose(); } catch { }
                throw;
            }
            finally { listener.Stop(); }
        }

        public void Dispose()
        {
            Client.Dispose(); Server.Dispose(); _client.Dispose(); _server.Dispose();
        }
    }
}
