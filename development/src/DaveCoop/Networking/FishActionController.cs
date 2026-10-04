using System;
using System.Text.Json;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // Unity-thread request consumer. The host rechecks native identity for a
    // read-only target probe. Effect capabilities stay unavailable until the
    // map, guest isolation, actor and native operation bridges are verified.
    internal sealed class FishActionController
    {
        private const int MaxPerFrame = 8;
        private const int MaxTraces = 1024;
        private HostFishActionGate _gate;
        private string _roomId;
        private long _nextRequest;
        private int _traces;
        public int PendingCount => _gate?.PendingCount ?? 0;
        public long HighestRequestId => _gate?.HighestRequestId ?? 0;
        public long ReceivedRequests { get; private set; }
        public long ReceivedResults { get; private set; }
        public long NativeLookupErrors { get; private set; }
        public string Status { get; private set; } = "Target check only.";

        public void BindRoom(SessionPeer main)
        {
            Clear();
            SessionSnapshot state = main.Snapshot;
            _roomId = state.RoomId;
            if (state.Role == SessionRole.Host) _gate = new HostFishActionGate(state.RoomId, state.RemotePlayerId);
        }

        public void SubmitProbe(SessionPeer guest, long entityId)
        {
            if (guest == null || entityId < 1) return;
            SessionSnapshot state = guest.Snapshot;
            if (state.Role != SessionRole.Guest || state.Phase != SessionPhase.Ready || state.RoomId != _roomId) return;
            if (_nextRequest == long.MaxValue) { Status = "Request IDs exhausted; reconnect."; return; }
            var request = new FishActionRequest
            {
                RequestId = ++_nextRequest, PlayerId = state.LocalPlayerId,
                SceneEpoch = state.SceneEpoch, SceneKey = state.SceneKey,
                Action = FishActionKind.ProbeTarget, TargetEntityId = entityId
            };
            try
            {
                if (!guest.PublishFishAction(request)) { Status = "Scene changed; target check not sent."; return; }
            }
            catch (Exception error)
            {
                // Publishing can fail after the GUI snapshot (including room
                // closure or bounded-queue rejection). Keep it inside OnGUI.
                Status = "Target check failed; check room status.";
                Trace("SEND_FAILED", new { request.RequestId, Error = error.GetType().Name + ": " + error.Message });
                return;
            }
            Status = "Checking target #" + entityId + ".";
            Trace("SENT", request);
        }

        public void Update(SessionPeer main, SessionPeer loopback, FishStateCapture fish,
            FishLifecycleHooks lifecycle, long publishedWorldRevision)
        {
            SessionSnapshot state = main.Snapshot;
            if (state.RoomId != _roomId) throw new InvalidOperationException("Fish action room ownership changed.");
            if (_gate != null)
            {
                int received = 0;
                while (received++ < MaxPerFrame && main.TryTakeRemoteFishAction(out ReceivedFishAction incoming))
                {
                    state = main.Snapshot;
                    ReceivedRequests++;
                    FishActionResult admission = _gate.Accept(incoming, state.Phase == SessionPhase.Ready,
                        state.SceneEpoch, state.SceneKey, main.Now);
                    PublishDecision(main, admission, "ADMISSION");
                }
                int executed = 0;
                while (executed++ < MaxPerFrame && _gate.TryTake(out ReceivedFishAction incoming))
                {
                    state = main.Snapshot;
                    HostActionFacts facts;
                    try { facts = ReadFacts(incoming.Request, state, fish, lifecycle, publishedWorldRevision, main.Now); }
                    catch (Exception error)
                    {
                        NativeLookupErrors++;
                        facts = new HostActionFacts
                        {
                            SceneEpoch = state.SceneEpoch, SceneKey = state.SceneKey,
                            WorldRevision = publishedWorldRevision, SampledAt = main.Now
                        };
                        Trace("LOOKUP_UNAVAILABLE", new { incoming.Request.RequestId, Error = error.GetType().Name + ": " + error.Message });
                    }
                    FishActionResult result = _gate.ValidateFresh(incoming.Request.RequestId, facts, main.Now, out HostFishActionPlan plan);
                    // This version has no effect adapter. Never dispatch or mark
                    // NativeStarted based on a selected fish or a valid packet.
                    if (plan != null) result = _gate.FinishNotStarted(plan, FishActionReason.NativeAdapterUnavailable);
                    PublishDecision(main, result, "DECISION");
                }
            }
            SessionPeer receiver = loopback ?? (state.Role == SessionRole.Guest ? main : null);
            if (receiver == null) return;
            int results = 0;
            while (results++ < MaxPerFrame * 2 && receiver.TryTakeRemoteFishActionResult(out FishActionResult result))
            {
                SessionSnapshot receiverState = receiver.Snapshot;
                if (receiverState.Phase != SessionPhase.Ready || result.SceneEpoch != receiverState.SceneEpoch || result.SceneKey != receiverState.SceneKey)
                { InvalidateScene(); continue; }
                ReceivedResults++;
                Status = result.Status == FishActionStatus.DryRunValidated ? "Target #" + result.TargetEntityId + " checked." :
                    result.Status == FishActionStatus.Queued ? "Target check queued." : "Target check: " + result.Reason;
                Trace("RECEIVED", result);
            }
        }

        private static HostActionFacts ReadFacts(FishActionRequest request, SessionSnapshot state,
            FishStateCapture fish, FishLifecycleHooks lifecycle, long revision, double now)
        {
            var facts = new HostActionFacts
            {
                SceneEpoch = state.SceneEpoch, SceneKey = state.SceneKey, WorldRevision = revision, SampledAt = now
            };
            if (state.Phase != SessionPhase.Ready || request.SceneEpoch != state.SceneEpoch || request.SceneKey != state.SceneKey ||
                !lifecycle.Healthy || request.TargetEntityId < 1 ||
                !fish.TryResolveNativeFish(request.SceneEpoch, request.TargetEntityId, out DR.AI.FishAISystem native)) return facts;
            HostEntityTarget? identity = fish.ResolveObservedPointer(native.Pointer.ToInt64());
            if (!identity.HasValue || identity.Value.SceneEpoch != request.SceneEpoch || identity.Value.EntityId != request.TargetEntityId) return facts;
            facts.TargetCaptured = native.IsFishCaptured;
            var damageable = native.FishDamageable;
            facts.TargetDead = damageable != null && damageable.m_IsDead;
            HostEntityTarget? currentIdentity = fish.ResolveObservedPointer(native.Pointer.ToInt64());
            if (!lifecycle.Healthy || !currentIdentity.HasValue || !currentIdentity.Value.Equals(identity.Value)) return facts;
            facts.Target = identity.Value; facts.TargetAvailable = true;
            // No available actor/loadout/authority flags are inferred from the
            // original host player, guest pose, fish visibility or native bool.
            return facts;
        }

        private void PublishDecision(SessionPeer main, FishActionResult result, string stage)
        {
            SessionSnapshot current = main.Snapshot;
            if (current.Phase != SessionPhase.Ready || result.SceneEpoch != current.SceneEpoch || result.SceneKey != current.SceneKey)
            {
                InvalidateScene();
                if (result.Status == FishActionStatus.Queued || result.Status == FishActionStatus.DryRunValidated)
                {
                    result = FishActions.Copy(result);
                    result.Status = FishActionStatus.Rejected; result.Reason = FishActionReason.SceneChanged;
                    result.WorldRevision = 0; result.OperationId = 0;
                }
            }
            // The session lock supplies the final scene fence: a pause can
            // still arrive between this snapshot and publication.
            bool published = main.PublishFishActionResult(result);
            if (!published) InvalidateScene();
            Trace(stage, new { Result = result, Published = published });
        }

        private void Trace(string stage, object value)
        {
            if (_traces++ >= MaxTraces) return;
            NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_ACTION_" + stage + ": " + JsonSerializer.Serialize(new
            {
                Room = _roomId, Value = value, NativeEffectsEnabled = false
            }));
        }

        public void InvalidateScene()
        {
            _gate?.InvalidateScene();
            Status = "Scene changed; select a current target.";
        }
        public void Clear()
        {
            _gate?.Close(); _gate = null; _roomId = null; _nextRequest = 0;
            ReceivedRequests = 0; ReceivedResults = 0; NativeLookupErrors = 0; Status = "Target check only.";
        }
    }
}
