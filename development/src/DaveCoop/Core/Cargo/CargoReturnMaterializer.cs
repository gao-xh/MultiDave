using System;

namespace DaveCoop.Core.Cargo
{
    public enum CargoMaterializerStage { NotEntered = 1, EnteredUnknown = 2 }

    public interface ICargoIngredientStorageBackend
    {
        void ValidateTarget(CargoEmployeeReturnPlan plan);
        void AddIngredients(CargoEmployeeReturnPlan plan);
    }

    public sealed class CargoMaterializerSnapshot
    {
        public CargoMaterializerStage Stage { get; internal set; }
        public bool AddAttempted { get; internal set; }
        public bool OriginalCallReturned { get; internal set; }
        public string Failure { get; internal set; }
        public bool StorageDeltaVerified => false;
        public bool SaveConfirmed => false;
    }

    // The existing ledger owns per-item dispatch arbitration. This coordinator
    // passes the bound immutable output, never another caller's six arguments.
    public sealed class CargoReturnMaterializer
    {
        private readonly ExpeditionCargoLedger _ledger;
        private readonly CargoEmployeeReturnPlan _plan;
        private readonly ICargoIngredientStorageBackend _backend;
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        private CargoMaterializerStage _stage = CargoMaterializerStage.NotEntered;
        private bool _busy, _invokingAdd, _addAttempted, _callReturned;
        private string _failure;
        public CargoReason LastEntryReason { get; private set; }
        public CargoMaterializerSnapshot Snapshot => new CargoMaterializerSnapshot
        {
            Stage = _stage, AddAttempted = _addAttempted, OriginalCallReturned = _callReturned, Failure = _failure
        };
        internal bool IsDispatching => Environment.CurrentManagedThreadId == _threadId && _busy &&
            _stage == CargoMaterializerStage.EnteredUnknown && _ledger.OwnsEmployeeReturnPlan(_plan, CargoReturnStage.EnteredUnknown);
        internal bool IsInvokingAdd => IsDispatching && _invokingAdd;

        public CargoReturnMaterializer(ExpeditionCargoLedger ledger, CargoEmployeeReturnPlan plan, ICargoIngredientStorageBackend backend)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        // A true return records one returned call plus target recheck only.
        // Actual storage delta and durable save need separate ledger evidence.
        public bool DispatchOnce(CargoStorageFacts freshFacts, double now)
        {
            if (Environment.CurrentManagedThreadId != _threadId || _busy || _stage != CargoMaterializerStage.NotEntered) return false;
            if (!_ledger.OwnsEmployeeReturnPlan(_plan, CargoReturnStage.Leased)) { LastEntryReason = CargoReason.InvalidStage; return false; }
            _busy = true;
            try
            {
                CargoResult entry = _ledger.EnterMaterialization(_plan.CaptureId, _plan.ProductIndex, freshFacts, now);
                LastEntryReason = entry.Reason;
                if (!entry.Accepted) return false;
                _stage = CargoMaterializerStage.EnteredUnknown; // Before any backend/native work.
                _backend.ValidateTarget(_plan);
                _addAttempted = true;
                _invokingAdd = true;
                try { _backend.AddIngredients(_plan); }
                finally { _invokingAdd = false; }
                _callReturned = true;
                _backend.ValidateTarget(_plan);
                return true;
            }
            catch (Exception error) { _failure = error.GetType().Name; return false; }
            finally { _busy = false; }
        }
    }
}
