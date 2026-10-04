using System;
using System.Collections.Generic;

namespace DaveCoop.Core.Cargo
{
    public enum FishYieldRecipeKind { OrdinaryPickup = 1, DeadFishBody = 2 }
    public enum FishYieldSelectionStage { NotEntered = 1, Selecting = 2, RawPlanHeld = 3, EnteredUnknown = 4 }

    // An employee's cooperative batch, not the game's interleaved Add/Roll
    // sequence. Leave one product slot for the single possible plus result.
    public sealed class FishYieldRecipe
    {
        public int FishDataTid { get; }
        public FishYieldRecipeKind Kind { get; }
        public int MainTierCount { get; }
        public int NoneBonusGrade { get; }
        public int LiftType { get; }
        public const int MaxMainTiers = CargoValues.MaxProductsPerCapture - 1;

        public FishYieldRecipe(int fishDataTid, FishYieldRecipeKind kind, int carvableCount, int noneBonusGrade, int liftType)
        {
            if (fishDataTid < 1 || !Enum.IsDefined(typeof(FishYieldRecipeKind), kind) || liftType < 0 || liftType > 3)
                throw new ArgumentException("Unsupported fish yield recipe.");
            int tiers = kind == FishYieldRecipeKind.DeadFishBody ? 1 : Math.Max(1, carvableCount);
            if (tiers > MaxMainTiers) throw new ArgumentException("The complete yield may exceed the cargo product limit.");
            if (noneBonusGrade == int.MaxValue) throw new ArgumentException("Plus selection grade would overflow.");
            FishDataTid = fishDataTid; Kind = kind; MainTierCount = tiers;
            NoneBonusGrade = noneBonusGrade; LiftType = liftType;
        }
    }

    // Immutable original selection and future commit arguments. BonusGrade is
    // not a resource's base grade, a final slot grade or a return quality.
    public sealed class FishSelectedDrop
    {
        public int Ordinal { get; }
        public int Tier { get; }
        public bool IsPlus { get; }
        public int ItemId { get; }
        public bool HasProduct => ItemId > 0;
        public int Count => 1;
        public int BonusGrade { get; }
        public int LiftType { get; }
        public bool ResourceHeld { get; }
        internal FishSelectedDrop(int ordinal, int tier, bool plus, int itemId, int bonus, int lift, bool held)
        { Ordinal = ordinal; Tier = tier; IsPlus = plus; ItemId = itemId; BonusGrade = bonus; LiftType = lift; ResourceHeld = held; }
        internal FishSelectedDrop WithHeldResource() => new FishSelectedDrop(Ordinal, Tier, IsPlus, ItemId, BonusGrade, LiftType, true);
    }

    public sealed class FishYieldSelectionSnapshot
    {
        public FishYieldSelectionStage Stage { get; internal set; }
        public bool PickupBonusKnown { get; internal set; }
        public int? PickupBonusGrade { get; internal set; }
        public bool PlusSelectionCompleted { get; internal set; }
        public FishSelectedDrop[] Drops { get; internal set; }
        public string Failure { get; internal set; }
        public bool FinalProductsVerified => false;
        public bool CaptureConfirmed => false;
    }

    // The actual native backend retains wrappers/strong handles privately.
    // No native resource, pointer or key crosses into these CLR snapshots.
    public interface IFishYieldSelectionBackend
    {
        void ValidateSource();
        int SelectPickupBonusGrade();
        int SelectMainItem(int fishDataTid, int tier);
        int SelectPlusItem(int fishDataTid, int selectionGrade);
        void HoldSelectedResource(FishSelectedDrop drop);
    }

    // Call sequencing on the existing ledger, not another authorization gate.
    // EnterSelection is the single arbitration point even if two coordinators
    // refer to the same lease. This object never invents facts or a receipt.
    public sealed partial class FishYieldSelection
    {
        private readonly ExpeditionCargoLedger _ledger;
        private readonly CargoSourceLease _lease;
        private readonly IFishYieldSelectionBackend _backend;
        private readonly FishYieldRecipe _recipe;
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private readonly List<FishSelectedDrop> _drops = new List<FishSelectedDrop>(CargoValues.MaxProductsPerCapture);
        private FishYieldSelectionStage _stage = FishYieldSelectionStage.NotEntered;
        private int? _bonus;
        private bool _plusCompleted, _busy;
        private string _failure;
        public CargoReason LastEntryReason { get; private set; }
        public FishYieldSelectionSnapshot Snapshot => new FishYieldSelectionSnapshot
        {
            Stage = _stage, PickupBonusKnown = _bonus.HasValue, PickupBonusGrade = _bonus,
            PlusSelectionCompleted = _plusCompleted, Drops = _drops.ToArray(), Failure = _failure
        };

        public FishYieldSelection(ExpeditionCargoLedger ledger, CargoSourceLease lease, FishYieldRecipe recipe, IFishYieldSelectionBackend backend)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _lease = lease ?? throw new ArgumentNullException(nameof(lease));
            _recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        // True means the raw selection and resource retention finished. It does
        // not mean capacity acceptance, native capture, loot or shared progress.
        public bool SelectOnce(CargoSourceFacts freshFacts, double now)
        {
            if (Environment.CurrentManagedThreadId != _threadId || _busy || _stage != FishYieldSelectionStage.NotEntered) return false;
            _busy = true;
            try
            {
                CargoResult entry = _ledger.EnterSelection(_lease, freshFacts, now);
                LastEntryReason = entry.Reason;
                if (!entry.Accepted) return false;
                _stage = FishYieldSelectionStage.Selecting;
                // Even a guard or getter failure after this mark is unknown.
                // A retry may not consume another grade/drop/pity selection.
                _backend.ValidateSource();
                _bonus = _recipe.Kind == FishYieldRecipeKind.OrdinaryPickup
                    ? _backend.SelectPickupBonusGrade() : _recipe.NoneBonusGrade;
                int plusGrade = checked(_bonus.Value + 1);
                for (int tier = 1; tier <= _recipe.MainTierCount; tier++)
                {
                    _backend.ValidateSource();
                    int id = _backend.SelectMainItem(_recipe.FishDataTid, tier);
                    HoldResult(tier, false, id, _bonus.Value);
                }
                _backend.ValidateSource();
                int plusId = _backend.SelectPlusItem(_recipe.FishDataTid, plusGrade);
                // Preserve a returned sentinel before any resource lookup.
                _plusCompleted = true;
                HoldResult(0, true, plusId, _recipe.NoneBonusGrade);
                _backend.ValidateSource();
                _stage = FishYieldSelectionStage.RawPlanHeld;
                return true;
            }
            catch (Exception error)
            {
                if (_stage == FishYieldSelectionStage.Selecting) _stage = FishYieldSelectionStage.EnteredUnknown;
                _failure = error.GetType().Name;
                return false;
            }
            finally { _busy = false; }
        }

        private void HoldResult(int tier, bool plus, int id, int bonus)
        {
            var drop = new FishSelectedDrop(_drops.Count, tier, plus, id, bonus, _recipe.LiftType, false);
            _drops.Add(drop);
            if (id == -1) return; // Explicit no-drop; do not roll again.
            if (id < 1) throw new InvalidOperationException("An unsupported original selection result was retained.");
            _backend.ValidateSource();
            _backend.HoldSelectedResource(drop);
            _drops[drop.Ordinal] = drop.WithHeldResource();
        }
    }
}
