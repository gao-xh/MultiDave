using DaveCoop.Core.World;

namespace DaveCoop.Networking
{
    // These describe observed native calls, never authoritative game outcomes.
    internal enum FishInteractionObservedKind
    {
        DamageCall = 1,
        HookCall = 2,
        FightWinCall = 3,
        PickupCall = 4,
        ProjectileFireCall = 5
    }

    internal enum FishInteractionMethodCode
    {
        FishOnTakeDamage = 1,
        SpecialFishOnTakeDamage = 2,
        FishHookedByProjectile = 3,
        FishWinFromProjectileFight = 4,
        MahoniCommonWinFromProjectileFight = 5,
        MahoniGeneralWinFromProjectileFight = 6,
        FishSuccessPickup = 7,
        HarpoonFire = 8
    }

    internal enum FishInteractionCallStage { Before = 1, After = 2 }

    // A bounded mailbox may carry these CLR values to the Unity-thread consumer.
    // Local pointers are diagnostic correlation tokens; never serialize them into
    // network packets or assume that a pointer still denotes the same pool life.
    internal sealed class FishInteractionEvent
    {
        public long ProcessSequence { get; }
        public long CallId { get; }
        public FishInteractionObservedKind ObservedKind { get; }
        public FishInteractionMethodCode OriginalMethodCode { get; }
        public FishInteractionCallStage Stage { get; }
        public long FishPointer { get; }
        public long ProjectilePointer { get; }
        public int CallbackThreadId { get; }
        public long? ResolvedEpoch { get; }
        public long? EntityId { get; }
        public long? Generation { get; }
        public int? DataTid { get; }
        public bool? DamageReturnObserved { get; }
        public bool CorrelationMatched { get; }

        public FishInteractionEvent(long processSequence, long callId, FishInteractionObservedKind observedKind,
            FishInteractionMethodCode originalMethodCode, FishInteractionCallStage stage,
            long fishPointer, long projectilePointer, int callbackThreadId,
            HostEntityTarget? originalBinding, bool? damageReturnObserved, bool correlationMatched)
        {
            ProcessSequence = processSequence; CallId = callId; ObservedKind = observedKind;
            OriginalMethodCode = originalMethodCode; Stage = stage;
            FishPointer = fishPointer; ProjectilePointer = projectilePointer;
            CallbackThreadId = callbackThreadId;
            ResolvedEpoch = originalBinding?.SceneEpoch; EntityId = originalBinding?.EntityId;
            Generation = originalBinding?.Generation; DataTid = originalBinding?.DataTid;
            DamageReturnObserved = damageReturnObserved; CorrelationMatched = correlationMatched;
        }
    }
}
