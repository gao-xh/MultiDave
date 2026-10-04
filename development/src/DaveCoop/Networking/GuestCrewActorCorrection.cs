using System;
using DaveCoop.Core.Crew;
using DaveCoop.Core.Session;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace DaveCoop.Networking
{
    // Correct only the actual temporary guest player's physical body. Its
    // original animation/camera remain local prediction; no host player writes.
    // Current startup/quarantine is a bounded source, not complete save isolation.
    internal sealed class GuestCrewActorCorrection
    {
        private readonly LocalAvatarCapture _local;
        private readonly Func<SessionPeer> _currentPeer;
        private readonly int _thread;
        private bool _busy, _failed;
        private int _reads;
        private long _enteredActor, _enteredState;
        public bool Failed => _failed;
        public string Status { get; private set; } = "WaitingForTemporaryGuest";
        public long Corrections { get; private set; }

        public GuestCrewActorCorrection(LocalAvatarCapture local, Func<SessionPeer> currentPeer, int thread)
        { _local = local; _currentPeer = currentPeer; _thread = thread; }

        public bool TryReadInputPosition(SessionPeer peer, out NVector3 position)
        {
            position = default;
            if (!Begin(peer, out SessionSnapshot state)) return false;
            try
            {
                if (!Capture(peer, state, out Stamp before)) return false;
                Vector2 xy = Read(peer, state, () => before.Body.position);
                if (!Finite(xy) || !Capture(peer, state, out Stamp after) || !Same(before, after)) return false;
                position = new NVector3(xy.x, xy.y, before.Z);
                return true;
            }
            catch (SourceExpired) { Status = "InputSourceChanged"; return false; }
            catch (Exception) { Fault("InputSourceReadFailed"); return false; }
            finally { _busy = false; }
        }

        public bool TryApply(SessionPeer peer, ReceivedCrewActorState receipt)
        {
            if (!Begin(peer, out SessionSnapshot state)) return false;
            bool writeEntered = false;
            try
            {
                ReceivedCrewActorState owned = CrewFrames.Copy(receipt);
                CrewFrames.Validate(owned);
                CrewActorState frame = owned.Frame;
                if (owned.RoomId != state.RoomId || frame.SceneEpoch != state.SceneEpoch || frame.SceneKey != state.SceneKey ||
                    frame.ActorRevision != state.CrewActorRevision || frame.StateRevision != state.CrewStateRevision || !frame.Active || !frame.Alive ||
                    peer.Now < owned.ReceivedAt || peer.Now - owned.ReceivedAt > CrewActorController.StateStaleSeconds ||
                    frame.ActorRevision < _enteredActor || (frame.ActorRevision == _enteredActor && frame.StateRevision <= _enteredState)) return false;
                if (!Capture(peer, state, out Stamp before) || frame.Position.Z != before.Z) return false;
                // Consume before either native setter. An unknown partial write
                // cannot be retried by the next FixedUpdate or newer callback.
                _enteredActor = frame.ActorRevision; _enteredState = frame.StateRevision;
                if (!Capture(peer, state, out Stamp check) || !Same(before, check)) return false;
                if (peer.Snapshot.CrewStateRevision != frame.StateRevision) return false;
                Read(peer, state, () => { writeEntered = true; before.Body.position = new Vector2 { x = frame.Position.X, y = frame.Position.Y }; return true; });
                if (!Capture(peer, state, out check) || !Same(before, check)) { Fault("SourceChangedAfterPositionWrite"); return false; }
                Read(peer, state, () => { before.Body.linearVelocity = new Vector2 { x = frame.Velocity.X, y = frame.Velocity.Y }; return true; });
                Vector2 actual = Read(peer, state, () => before.Body.position);
                if (!Capture(peer, state, out check) || !Same(before, check) || !Finite(actual) ||
                    Math.Abs(actual.x - frame.Position.X) > 0.0001f || Math.Abs(actual.y - frame.Position.Y) > 0.0001f)
                { Fault("CorrectionReadbackUnknown"); return false; }
                Corrections++; Status = "HostPositionApplied"; return true;
            }
            catch (SourceExpired)
            { if (writeEntered) Fault("CorrectionSourceChangedAfterWrite"); else Status = "CorrectionSourceChanged"; return false; }
            catch (Exception) { Fault("CorrectionWriteUnknown"); return false; }
            finally { _busy = false; }
        }

        private bool Begin(SessionPeer peer, out SessionSnapshot state)
        {
            state = null;
            if (_failed || Environment.CurrentManagedThreadId != _thread) return false;
            if (_busy) { Fault("ReentrantCorrection"); return false; }
            state = peer?.Snapshot;
            if (!Current(peer, state)) return false;
            _reads = 0; _busy = true; return true;
        }

        private bool Current(SessionPeer peer, SessionSnapshot state)
        {
            if (_failed || Environment.CurrentManagedThreadId != _thread || peer == null || state == null ||
                !ReferenceEquals(peer, _currentPeer())) return false;
            var startup = NativeGuestInitializationController.Current;
            if (startup == null || startup.Failed || startup.ConfirmedUnityThreadId != _thread) return false;
            SessionSnapshot current = peer.Snapshot;
            return current.Role == SessionRole.Guest && current.Phase == SessionPhase.Ready &&
                current.LocalPlayerId == 2 && current.RemotePlayerId == 1 && current.LocalUsesCrewActor && current.RemoteUsesCrewActor &&
                current.RoomId == state.RoomId && current.SceneEpoch == state.SceneEpoch && current.SceneKey == state.SceneKey &&
                current.CrewActorRevision == state.CrewActorRevision;
        }

        private T Read<T>(SessionPeer peer, SessionSnapshot state, Func<T> read)
        {
            if (++_reads > 2048) throw new InvalidOperationException("Guest actor read budget exhausted.");
            if (!Current(peer, state)) throw new SourceExpired();
            T result = read();
            if (!Current(peer, state)) throw new SourceExpired();
            return result;
        }

        private bool Capture(SessionPeer peer, SessionSnapshot state, out Stamp stamp)
        {
            stamp = null;
            InGameManager manager = _local.Manager; PlayerCharacter player = _local.Player;
            if (!Live(peer, state, manager) || !Live(peer, state, player) ||
                !Read(peer, state, () => _local.IsAvailable)) return false;
            PlayerCharacter owner = Read(peer, state, () => manager._playerCharacter_k__BackingField);
            if (!Live(peer, state, owner) || owner.Pointer != player.Pointer) return false;
            GameObject root = Read(peer, state, () => player.gameObject);
            if (!Live(peer, state, root)) return false;
            var scene = Read(peer, state, () => root.scene);
            if (!Read(peer, state, () => scene.IsValid()) || !Read(peer, state, () => scene.isLoaded) ||
                scene.handle != _local.SceneHandle || Read(peer, state, () => scene.name) != state.SceneKey ||
                !Read(peer, state, () => root.activeInHierarchy)) return false;
            var startup = NativeGuestInitializationController.Current;
            if (!Read(peer, state, () => startup.CanDisplayHostFish(peer, scene.handle))) return false;
            CharacterController2D controller = Read(peer, state, () => player._Controller2D_k__BackingField);
            if (!Live(peer, state, controller)) return false;
            Rigidbody2D body = Read(peer, state, () => controller.m_Rigidbody);
            if (!Live(peer, state, body) || !Read(peer, state, () => body.simulated)) return false;
            Collider2D collider = Read(peer, state, () => controller.m_CharacterCollider);
            if (ReferenceEquals(collider, null) || collider.Pointer == IntPtr.Zero ||
                Read(peer, state, () => collider.m_CachedPtr) == IntPtr.Zero || !Read(peer, state, () => collider.enabled)) return false;
            Rigidbody2D attached = Read(peer, state, () => collider.attachedRigidbody);
            if (!Live(peer, state, attached) || attached.Pointer != body.Pointer) return false;
            GameObject bodyRoot = Read(peer, state, () => body.gameObject);
            if (!Live(peer, state, bodyRoot) || !Read(peer, state, () => bodyRoot.activeInHierarchy) ||
                Read(peer, state, () => bodyRoot.scene.handle) != scene.handle) return false;
            Transform bodyTransform = Read(peer, state, () => body.transform);
            if (!Live(peer, state, bodyTransform)) return false;
            float z = Read(peer, state, () => bodyTransform.position.z);
            if (!float.IsFinite(z)) return false;
            stamp = new Stamp
            {
                Manager = manager.Pointer, Player = player.Pointer, Controller = controller.Pointer, Root = root.Pointer,
                ManagerUnity = Read(peer, state, () => manager.m_CachedPtr), PlayerUnity = Read(peer, state, () => player.m_CachedPtr),
                ControllerUnity = Read(peer, state, () => controller.m_CachedPtr), RootUnity = Read(peer, state, () => root.m_CachedPtr),
                Body = body, BodyUnity = Read(peer, state, () => body.m_CachedPtr), Scene = scene.handle, Z = z,
                Collider = collider.Pointer, ColliderUnity = Read(peer, state, () => collider.m_CachedPtr),
                ColliderClass = Read(peer, state, () => IL2CPP.il2cpp_object_get_class(collider.Pointer)),
                PlayerId = Read(peer, state, () => player.GetInstanceID()), BodyId = Read(peer, state, () => body.GetInstanceID())
            };
            return stamp.PlayerId == _local.PlayerId && Current(peer, state);
        }

        private bool Live<T>(SessionPeer peer, SessionSnapshot state, T value) where T : UnityEngine.Object
        {
            if (ReferenceEquals(value, null) || value.Pointer == IntPtr.Zero) return false;
            IntPtr expected = Read(peer, state, () => Il2CppClassPointerStore<T>.NativeClassPtr);
            return Read(peer, state, () => value.m_CachedPtr) != IntPtr.Zero && expected != IntPtr.Zero &&
                Read(peer, state, () => IL2CPP.il2cpp_object_get_class(value.Pointer)) == expected;
        }
        private static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);
        private static bool Same(Stamp a, Stamp b) => a.Manager == b.Manager && a.Player == b.Player && a.Controller == b.Controller &&
            a.Root == b.Root && a.ManagerUnity == b.ManagerUnity && a.PlayerUnity == b.PlayerUnity &&
            a.ControllerUnity == b.ControllerUnity && a.RootUnity == b.RootUnity && a.Body.Pointer == b.Body.Pointer &&
            a.Collider == b.Collider && a.ColliderUnity == b.ColliderUnity && a.ColliderClass == b.ColliderClass &&
            a.BodyUnity == b.BodyUnity && a.Scene == b.Scene && a.PlayerId == b.PlayerId && a.BodyId == b.BodyId && a.Z == b.Z;
        private void Fault(string reason) { _failed = true; Status = reason; }
        private sealed class SourceExpired : Exception { }
        private sealed class Stamp
        {
            public IntPtr Manager, Player, Controller, Root, ManagerUnity, PlayerUnity, ControllerUnity, RootUnity, BodyUnity;
            public IntPtr Collider, ColliderUnity, ColliderClass;
            public Rigidbody2D Body;
            public int Scene, PlayerId, BodyId;
            public float Z;
        }
    }
}
