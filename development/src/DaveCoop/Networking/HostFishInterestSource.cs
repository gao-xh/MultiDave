using System;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;
using UnityEngine;

namespace DaveCoop.Networking
{
    // Native references never leave this Unity-thread source window.
    internal sealed class HostFishInterestWindow
    {
        internal HostFishInterestSource Owner { get; }
        internal SessionPeer Peer { get; }
        internal long SourceRevision { get; }
        internal int ManagerId { get; }
        internal int PlayerId { get; }
        public HostFishInterest Interest { get; }
        public InGameManager Manager { get; }
        public PlayerCharacter LocalPlayer { get; }
        public Transform LocalPlayerTransform { get; }
        public Vector3 LocalPosition { get; }
        public int SceneHandle { get; }

        internal HostFishInterestWindow(HostFishInterestSource owner, SessionPeer peer, long revision,
            HostFishInterest interest, InGameManager manager, int managerId, PlayerCharacter player,
            int playerId, Transform transform, Vector3 localPosition, int sceneHandle)
        {
            Owner = owner; Peer = peer; SourceRevision = revision; Interest = interest;
            Manager = manager; ManagerId = managerId; LocalPlayer = player; PlayerId = playerId;
            LocalPlayerTransform = transform; LocalPosition = localPosition; SceneHandle = sceneHandle;
        }
    }

    // Only a received frame from the current real host peer can produce a
    // bounded observation. No remote avatar, pose interpolation or action facts.
    internal sealed class HostFishInterestSource
    {
        private readonly Func<SessionPeer> _currentMain;
        private readonly Func<bool> _localTest;
        private readonly LocalAvatarCapture _local;
        private readonly int _mainThreadId;
        private SessionPeer _boundPeer;
        private HostFishInterestBuffer _buffer;
        private HostFishInterestWindow _lastWindow;
        private LocalStamp _lastStamp;
        private long _revision;
        private bool _busy, _failed, _unityObserved;
        public int UnityThreadId => _unityObserved ? _mainThreadId : 0;
        public bool Failed => _failed;
        public bool IsReading => _busy;
        public string Status { get; private set; } = "Empty";
        public long AcceptedReceipts { get; private set; }
        public long RejectedReceipts { get; private set; }
        public long ReadErrors { get; private set; }
        public long HighestPacketSequence => _buffer?.HighestPacketSequence ?? 0;

        public HostFishInterestSource(Func<SessionPeer> currentMain, Func<bool> localTest,
            LocalAvatarCapture local, int mainThreadId)
        {
            _currentMain = currentMain ?? throw new ArgumentNullException(nameof(currentMain));
            _localTest = localTest ?? throw new ArgumentNullException(nameof(localTest));
            _local = local ?? throw new ArgumentNullException(nameof(local));
            if (mainThreadId < 1) throw new ArgumentOutOfRangeException(nameof(mainThreadId));
            _mainThreadId = mainThreadId;
        }

        // Called only by the real NetworkController.Update, never by a hook or
        // receipt. Construction on a CLR thread alone does not authorize reads.
        public void ConfirmUnityUpdate()
        {
            if (_failed) return;
            if (Environment.CurrentManagedThreadId != _mainThreadId) { Fault("WrongUpdateThread"); return; }
            _unityObserved = true;
        }

        public bool Accept(SessionPeer peer, ReceivedFrame receipt)
        {
            if (!Begin()) { RejectedReceipts++; return false; }
            try
            {
                if (!TryBind(peer, out SessionSnapshot state)) { RejectedReceipts++; return false; }
                long revision = _revision;
                if (!TryLocal(peer, state, revision, out LocalStamp before))
                { Clear("LocalSourceUnavailable"); RejectedReceipts++; return false; }
                if (!_buffer.TryAccept(receipt, state, peer.Now))
                { Status = _buffer.Status; _lastWindow = null; RejectedReceipts++; return false; }
                if (!TryLocal(peer, state, revision, out LocalStamp after) || !Same(before, after) ||
                    !PureCurrent(peer, state, revision))
                { Clear("SourceChangedDuringReceipt"); RejectedReceipts++; return false; }
                _lastWindow = null; _lastStamp = null; AcceptedReceipts++; Status = "Current"; return true;
            }
            catch (Exception) { Fault("ReceiptReadFailed"); RejectedReceipts++; return false; }
            finally { _busy = false; }
        }

        public bool TryRead(out HostFishInterest interest)
        {
            interest = null;
            if (!TryCapture(out HostFishInterestWindow window)) return false;
            interest = window.Interest; return true;
        }

        public bool TryCapture(out HostFishInterestWindow window)
        {
            window = null;
            if (!Begin()) return false;
            try
            {
                SessionPeer peer = _currentMain();
                if (!TryBind(peer, out SessionSnapshot state)) return false;
                long revision = _revision;
                if (!_buffer.TryRead(state, peer.Now, out HostFishInterest interest))
                { Status = _buffer.Status; _lastWindow = null; return false; }
                if (!TryLocal(peer, state, revision, out LocalStamp before) ||
                    !TryLocal(peer, state, revision, out LocalStamp after) || !Same(before, after) ||
                    !_buffer.TryRead(peer.Snapshot, peer.Now, out HostFishInterest current) ||
                    !ReferenceEquals(current, interest) || !PureCurrent(peer, state, revision))
                { Clear("SourceChangedDuringCapture"); return false; }
                window = new HostFishInterestWindow(this, peer, revision, interest, after.Manager,
                    after.ManagerId, after.Player, after.PlayerId, after.Transform, after.Position, after.SceneHandle);
                _lastWindow = window; _lastStamp = after; Status = "Current"; return true;
            }
            catch (Exception) { Fault("SourceReadFailed"); return false; }
            finally { _busy = false; }
        }

        public bool IsCurrent(HostFishInterestWindow window)
        {
            if (window == null || !ReferenceEquals(window.Owner, this) || !ReferenceEquals(_lastWindow, window)) return false;
            if (!Begin()) return false;
            try
            {
                SessionPeer peer = window.Peer;
                if (!TryBind(peer, out SessionSnapshot state) || window.SourceRevision != _revision ||
                    !_buffer.TryRead(state, peer.Now, out HostFishInterest interest) ||
                    !ReferenceEquals(interest, window.Interest) ||
                    !TryLocal(peer, state, window.SourceRevision, out LocalStamp current) ||
                    !ReferenceEquals(current.Manager, window.Manager) || current.ManagerId != window.ManagerId ||
                    !ReferenceEquals(current.Player, window.LocalPlayer) || current.PlayerId != window.PlayerId ||
                    current.SceneHandle != window.SceneHandle ||
                    !Same(current, _lastStamp) || !PureCurrent(peer, state, window.SourceRevision) ||
                    !_buffer.TryRead(peer.Snapshot, peer.Now, out interest) || !ReferenceEquals(interest, window.Interest))
                { Clear("InterestWindowExpired"); return false; }
                return true;
            }
            catch (Exception) { Fault("WindowReadFailed"); return false; }
            finally { _busy = false; }
        }

        public void Clear(string reason = "Cleared")
        {
            _buffer?.Clear(reason); _lastWindow = null; _lastStamp = null;
            if (_revision < long.MaxValue) _revision++;
            else _failed = true;
            Status = reason ?? "Cleared";
        }

        private bool Begin()
        {
            if (_failed) return false;
            if (!_unityObserved) { Clear("AwaitingUnityUpdate"); return false; }
            if (Environment.CurrentManagedThreadId != _mainThreadId) { Fault("WrongThread"); return false; }
            if (_busy) { Fault("ReentrantSourceRead"); return false; }
            _busy = true; return true;
        }

        private bool TryBind(SessionPeer peer, out SessionSnapshot state)
        {
            state = null;
            if (_failed || !_unityObserved || Environment.CurrentManagedThreadId != _mainThreadId || _localTest() ||
                peer == null || !ReferenceEquals(peer, _currentMain()))
            { Clear("PeerUnavailableOrLocalTest"); return false; }
            state = peer.Snapshot;
            if (state.Role != SessionRole.Host || state.Phase != SessionPhase.Ready ||
                state.LocalPlayerId != 1 || state.RemotePlayerId != 2)
            { Clear("HostSceneNotReady"); return false; }
            if (!ReferenceEquals(peer, _boundPeer))
            {
                Clear("PeerChanged"); _boundPeer = peer; _buffer = new HostFishInterestBuffer(state.RoomId);
            }
            return true;
        }

        private bool PureCurrent(SessionPeer peer, SessionSnapshot state, long revision)
        {
            if (_failed || !_unityObserved || Environment.CurrentManagedThreadId != _mainThreadId || _revision != revision ||
                _localTest() || !ReferenceEquals(peer, _boundPeer) || !ReferenceEquals(peer, _currentMain())) return false;
            SessionSnapshot current = peer.Snapshot;
            return current.Role == SessionRole.Host && current.Phase == SessionPhase.Ready &&
                current.LocalPlayerId == 1 && current.RemotePlayerId == 2 && current.RoomId == state.RoomId &&
                current.SceneEpoch == state.SceneEpoch && current.SceneKey == state.SceneKey;
        }

        private T Read<T>(Func<T> read, SessionPeer peer, SessionSnapshot state, long revision)
        {
            if (!PureCurrent(peer, state, revision)) throw new InvalidOperationException("Interest source expired before native read.");
            T value = read();
            if (!PureCurrent(peer, state, revision)) throw new InvalidOperationException("Interest source expired during native read.");
            return value;
        }

        private bool TryLocal(SessionPeer peer, SessionSnapshot state, long revision, out LocalStamp stamp)
        {
            stamp = null;
            if (!PureCurrent(peer, state, revision) || !Read(() => _local.IsAvailable, peer, state, revision)) return false;
            InGameManager manager = _local.Manager; PlayerCharacter player = _local.Player;
            if (ReferenceEquals(manager, null) || ReferenceEquals(player, null)) return false;
            IntPtr managerPointer = Read(() => manager.Pointer, peer, state, revision);
            IntPtr managerUnity = Read(() => manager.m_CachedPtr, peer, state, revision);
            IntPtr playerPointer = Read(() => player.Pointer, peer, state, revision);
            IntPtr playerUnity = Read(() => player.m_CachedPtr, peer, state, revision);
            if (managerPointer == IntPtr.Zero || managerUnity == IntPtr.Zero || playerPointer == IntPtr.Zero || playerUnity == IntPtr.Zero) return false;
            if (!OwnerMatches(manager, playerPointer, playerUnity, peer, state, revision)) return false;
            int managerId = Read(() => manager.GetInstanceID(), peer, state, revision);
            int playerId = Read(() => player.GetInstanceID(), peer, state, revision);
            GameObject gameObject = Read(() => player.gameObject, peer, state, revision);
            if (ReferenceEquals(gameObject, null)) return false;
            IntPtr rootPointer = Read(() => gameObject.Pointer, peer, state, revision);
            IntPtr rootUnity = Read(() => gameObject.m_CachedPtr, peer, state, revision);
            var scene = Read(() => gameObject.scene, peer, state, revision);
            int sceneHandle = Read(() => scene.handle, peer, state, revision);
            string sceneName = Read(() => scene.name, peer, state, revision);
            bool sceneValid = Read(() => scene.IsValid(), peer, state, revision);
            bool sceneLoaded = Read(() => scene.isLoaded, peer, state, revision);
            Transform transform = Read(() => player.transform, peer, state, revision);
            if (ReferenceEquals(transform, null)) return false;
            IntPtr transformPointer = Read(() => transform.Pointer, peer, state, revision);
            IntPtr transformUnity = Read(() => transform.m_CachedPtr, peer, state, revision);
            Vector3 position = Read(() => transform.position, peer, state, revision);
            if (!sceneValid || !sceneLoaded || sceneHandle == 0 || sceneHandle != _local.SceneHandle || sceneName != state.SceneKey ||
                rootPointer == IntPtr.Zero || rootUnity == IntPtr.Zero || transformPointer == IntPtr.Zero || transformUnity == IntPtr.Zero ||
                playerId != _local.PlayerId || !Finite(position) || !ReferenceEquals(manager, _local.Manager) || !ReferenceEquals(player, _local.Player) ||
                !OwnerMatches(manager, playerPointer, playerUnity, peer, state, revision) ||
                !Read(() => _local.IsAvailable, peer, state, revision) || !PureCurrent(peer, state, revision)) return false;
            stamp = new LocalStamp { Manager = manager, ManagerId = managerId, Player = player, PlayerId = playerId,
                Transform = transform, Position = position, SceneHandle = sceneHandle,
                ManagerPointer = managerPointer, ManagerUnity = managerUnity, PlayerPointer = playerPointer, PlayerUnity = playerUnity,
                RootPointer = rootPointer, RootUnity = rootUnity, TransformPointer = transformPointer, TransformUnity = transformUnity };
            return true;
        }

        private bool OwnerMatches(InGameManager manager, IntPtr playerPointer, IntPtr playerUnity,
            SessionPeer peer, SessionSnapshot state, long revision)
        {
            PlayerCharacter owner = Read(() => manager._playerCharacter_k__BackingField, peer, state, revision);
            return !ReferenceEquals(owner, null) && Read(() => owner.Pointer, peer, state, revision) == playerPointer &&
                Read(() => owner.m_CachedPtr, peer, state, revision) == playerUnity;
        }

        private void Fault(string reason) { ReadErrors++; _failed = true; Clear(reason); }
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        private static bool Same(LocalStamp a, LocalStamp b) => a != null && b != null &&
            ReferenceEquals(a.Manager, b.Manager) && a.ManagerId == b.ManagerId &&
            ReferenceEquals(a.Player, b.Player) && a.PlayerId == b.PlayerId &&
            a.SceneHandle == b.SceneHandle && a.Position.x == b.Position.x && a.Position.y == b.Position.y && a.Position.z == b.Position.z &&
            a.ManagerPointer == b.ManagerPointer && a.ManagerUnity == b.ManagerUnity &&
            a.PlayerPointer == b.PlayerPointer && a.PlayerUnity == b.PlayerUnity &&
            a.RootPointer == b.RootPointer && a.RootUnity == b.RootUnity &&
            a.TransformPointer == b.TransformPointer && a.TransformUnity == b.TransformUnity;
        private sealed class LocalStamp
        {
            public InGameManager Manager;
            public int ManagerId;
            public PlayerCharacter Player;
            public int PlayerId;
            public Transform Transform;
            public Vector3 Position;
            public int SceneHandle;
            public IntPtr ManagerPointer, ManagerUnity, PlayerPointer, PlayerUnity;
            public IntPtr RootPointer, RootUnity, TransformPointer, TransformUnity;
        }
    }
}
