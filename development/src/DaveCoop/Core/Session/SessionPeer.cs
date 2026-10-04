using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Transport;
using DaveCoop.Core.World;
using DaveCoop.Core.Actions;

namespace DaveCoop.Core.Session
{
    // Thread-safe boundary for a main-thread game adapter. Only numeric DTOs cross it.
    public sealed class SessionPeer : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly SessionMachine _machine;
        private readonly FramedConnection _connection;
        private readonly SessionOptions _options;
        private readonly CancellationTokenSource _lifetime;
        private readonly SemaphoreSlim _wake = new SemaphoreSlim(0, 1);

        private SessionPeer(SessionRole role, FramedConnection connection, HandshakeResult identity,
            SessionOptions options, CancellationToken cancellation)
        {
            _connection = connection; _options = options;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            _machine = new SessionMachine(role, identity, Now, options);
            Completion = RunAsync();
        }

        public double Now => _clock.Elapsed.TotalSeconds;
        public Task Completion { get; }
        public SessionSnapshot Snapshot { get { lock (_gate) return _machine.Snapshot; } }

        public static Task<SessionPeer> AcceptAsync(FramedConnection connection, PeerIdentity local,
            string roomId, CancellationToken cancellation, SessionOptions options = null)
            => StartAsync(SessionRole.Host, connection, local, roomId, cancellation, options);

        public static Task<SessionPeer> JoinAsync(FramedConnection connection, PeerIdentity local,
            CancellationToken cancellation, SessionOptions options = null)
            => StartAsync(SessionRole.Guest, connection, local, null, cancellation, options);

        private static async Task<SessionPeer> StartAsync(SessionRole role, FramedConnection connection,
            PeerIdentity local, string roomId, CancellationToken cancellation, SessionOptions options)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            try
            {
                SessionOptions validated = (options ?? new SessionOptions()).CopyValidated();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(validated.HandshakeTimeoutSeconds));
                HandshakeResult identity = role == SessionRole.Host
                    ? await Handshake.AcceptAsync(connection, local, roomId, timeout.Token).ConfigureAwait(false)
                    : await Handshake.JoinAsync(connection, local, timeout.Token).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                return new SessionPeer(role, connection, identity, validated, cancellation);
            }
            catch { connection.Dispose(); throw; }
        }

        public void SetLocalScene(SceneDescriptor scene)
        {
            lock (_gate)
            {
                try { _machine.SetLocalScene(scene, Now); SignalWriter(); }
                catch (ProtocolException error) { Terminate(error.Message); throw; }
            }
        }

        public bool PublishFrame(PlayerFrame frame)
        {
            lock (_gate)
            {
                bool accepted = _machine.PublishFrame(frame, Now);
                if (accepted) SignalWriter();
                return accepted;
            }
        }

        public bool TryTakeRemoteFrame(out ReceivedFrame frame)
        {
            lock (_gate) return _machine.TryTakeRemoteFrame(out frame);
        }

        public bool PublishWorld(WorldSnapshot snapshot)
        {
            lock (_gate)
            {
                bool accepted = _machine.PublishWorld(snapshot, Now);
                if (accepted) SignalWriter();
                return accepted;
            }
        }

        public bool TryTakeRemoteWorld(out WorldSnapshot snapshot)
        {
            lock (_gate) return _machine.TryTakeRemoteWorld(out snapshot);
        }

        public bool PublishMapRoute(MapRouteSelection route)
        {
            lock (_gate)
            {
                try
                {
                    bool accepted = _machine.PublishMapRoute(route, Now);
                    if (accepted) SignalWriter();
                    return accepted;
                }
                catch (ProtocolException error) { Terminate(error.Message); throw; }
            }
        }

        public bool PublishMapIgpChoice(MapIgpChoice choice)
        {
            lock (_gate)
            {
                try
                {
                    bool accepted = _machine.PublishMapIgpChoice(choice, Now);
                    // A false queue-overflow result still queued a retirement.
                    // Wake the writer to make that cancellation observable.
                    SignalWriter();
                    return accepted;
                }
                catch (ProtocolException error) { Terminate(error.Message); throw; }
            }
        }

        public bool RetireMapChoices(string reason)
        {
            lock (_gate)
            {
                try
                {
                    bool accepted = _machine.RetireMapChoices(reason, Now);
                    if (accepted) SignalWriter();
                    return accepted;
                }
                catch (ProtocolException error) { Terminate(error.Message); throw; }
            }
        }

        public bool TryTakeRemoteMapChoices(out MapChoiceSnapshot choices)
        {
            lock (_gate) return _machine.TryTakeRemoteMapChoices(out choices);
        }

        public bool PublishFishAction(FishActionRequest request)
        {
            lock (_gate)
            {
                try
                {
                    bool accepted = _machine.PublishFishAction(request, Now);
                    if (accepted) SignalWriter();
                    return accepted;
                }
                catch (ProtocolException error) { Terminate(error.Message); throw; }
            }
        }

        public bool PublishFishActionResult(FishActionResult result)
        {
            lock (_gate)
            {
                try
                {
                    bool accepted = _machine.PublishFishActionResult(result, Now);
                    if (accepted) SignalWriter();
                    return accepted;
                }
                catch (ProtocolException error) { Terminate(error.Message); throw; }
            }
        }

        public bool TryTakeRemoteFishAction(out ReceivedFishAction action)
        {
            lock (_gate) return _machine.TryTakeRemoteFishAction(out action);
        }

        public bool TryTakeRemoteFishActionResult(out FishActionResult result)
        {
            lock (_gate) return _machine.TryTakeRemoteFishActionResult(out result);
        }

        public bool TryTakeEvent(out SessionEvent item)
        {
            lock (_gate) return _machine.TryTakeEvent(out item);
        }

        public async Task StopAsync(string reason = "Left room.")
        {
            if (Snapshot.Phase != SessionPhase.Closed)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    await _connection.SendAsync(new WirePacket
                    {
                        Kind = PacketKind.Leave, RoomId = Snapshot.RoomId, Reason = reason
                    }, timeout.Token).ConfigureAwait(false);
                }
                catch { /* Teardown must also work when the peer has already disappeared. */ }
                Terminate(reason);
            }
            await Completion.ConfigureAwait(false);
        }

        private async Task RunAsync()
        {
            Task[] loops = { ReceiveLoopAsync(), SendLoopAsync(), TickLoopAsync() };
            Task first = await Task.WhenAny(loops).ConfigureAwait(false);
            string reason = "Connection closed.";
            try { await first.ConfigureAwait(false); }
            catch (OperationCanceledException) { reason = "Session cancelled."; }
            catch (Exception error) { reason = error.Message; }
            Terminate(reason);
            try { await Task.WhenAll(loops).ConfigureAwait(false); } catch { }
            // Disposing synchronization primitives while main-thread callbacks are still
            // possible would race those callbacks. They contain no socket/native handles.
        }

        private async Task ReceiveLoopAsync()
        {
            while (true)
            {
                WirePacket packet = await _connection.ReceiveAsync(_lifetime.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    _machine.Receive(packet, Now); SignalWriter();
                    if (_machine.Snapshot.Phase == SessionPhase.Closed) return;
                }
            }
        }

        private async Task SendLoopAsync()
        {
            while (true)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                WirePacket packet;
                lock (_gate) _machine.TryTakePacket(out packet);
                if (packet == null) await _wake.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                else await _connection.SendAsync(packet, _lifetime.Token).ConfigureAwait(false);
            }
        }

        private async Task TickLoopAsync()
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.TickIntervalSeconds), _lifetime.Token).ConfigureAwait(false);
                lock (_gate) { _machine.Tick(Now); SignalWriter(); }
            }
        }

        private void SignalWriter()
        {
            // Called under _gate; at most one wake-up is retained regardless of frame rate.
            if (_wake.CurrentCount == 0) _wake.Release();
        }

        private void Terminate(string reason)
        {
            lock (_gate)
            {
                _machine.Close(reason);
                if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
                _connection.Dispose();
            }
        }

        public void Dispose() => Terminate("Session disposed.");
    }
}
