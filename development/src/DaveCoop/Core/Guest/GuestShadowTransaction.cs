using System;

namespace DaveCoop.Core.Guest
{
    public enum GuestShadowRoot { GameData = 1, PlayerData = 2, PlayerInteraction = 3, PhotoData = 4, UserOption = 5, IngredientsCache = 6 }
    // OwnedMixed applies only to the composite IngredientsCache step: every
    // field is proven original or detached, but the pair is incomplete. The
    // backend must inspect/restore individual fields; never rewrite a pair blind.
    public enum GuestShadowRootReadback { Unknown = 0, Original = 1, Detached = 2, Foreign = 3, OwnedMixed = 4 }
    public enum GuestShadowStage { Created, Installing, ShadowInstalled, Restoring, RestoredFenced, Released, Failed }
    public enum GuestShadowReason
    {
        None, Duplicate, WrongThread, BindingChanged, BoundaryUnavailable, LeaseAlreadyUsed,
        FenceUnavailable, CaptureFailed, PrepareFailed, InstallFailed, ValidationFailed,
        ReadbackUnknown, ForeignRoot, RestoreFailed, QuiescenceUnavailable, FenceRemovalFailed,
        ReferenceReleaseFailed, InvalidStage, Reentrant, Faulted
    }

    // The production backend owns actual manager/root references, thread and
    // single-use source binding. No caller-supplied bool or scalar DTO grants
    // native permission. A synthetic backend tests ordering and compensation only.
    public interface IGuestShadowBackend
    {
        Guid HostBindingId { get; }
        Guid LeaseId { get; }
        int UnityThreadId { get; }
        bool TryClaimLease();
        bool BindingIsCurrent();
        bool CanEnterBoundary();
        bool InstallOutputFence();
        bool FenceIsHealthy();
        bool FenceIsActive();
        bool CaptureOriginal();
        bool PrepareDetached();
        bool InstallRoot(GuestShadowRoot root);
        GuestShadowRootReadback ReadRoot(GuestShadowRoot root);
        bool ValidateRoots();
        bool RestoreRoot(GuestShadowRoot root);
        bool ConfirmOriginalManagersAndRoots();
        bool HasQuiescentBoundary();
        bool RemoveOutputFence();
        bool ReleaseReferences();
    }

    public sealed class GuestShadowResult
    {
        public GuestShadowReason Reason { get; }
        public string Message { get; }
        public bool Accepted => Reason == GuestShadowReason.None;
        internal GuestShadowResult(GuestShadowReason reason, string message) { Reason = reason; Message = message; }
    }

    public sealed class GuestShadowSnapshot
    {
        public Guid HostBindingId { get; internal set; }
        public Guid LeaseId { get; internal set; }
        public GuestShadowStage Stage { get; internal set; }
        public bool RootShadowInstalled { get; internal set; }
        // Retained is a cleanup obligation, not a claim that hooks are healthy.
        public bool FenceRetained { get; internal set; }
        public bool FenceIntegrityLost { get; internal set; }
        public bool ReferencesRetained { get; internal set; }
        public bool OriginalsRestored { get; internal set; }
        public string Fault { get; internal set; }
        public GuestShadowRootReadback[] Roots { get; internal set; }
        public bool GuestStateIsolated => false;
        public bool NativePermission => false;
        public bool WorldAuthority => false;
        public bool CargoAuthority => false;
    }

    // Single-threaded and bound to one backend lease for its lifetime. Failed
    // writes may have entered native code: readback, never repetition, resolves
    // their outcome. Strong references stay owned until verified final cleanup.
    public sealed class GuestShadowTransaction
    {
        private static readonly GuestShadowRoot[] Order = { GuestShadowRoot.GameData, GuestShadowRoot.PlayerData,
            GuestShadowRoot.PlayerInteraction, GuestShadowRoot.PhotoData, GuestShadowRoot.UserOption, GuestShadowRoot.IngredientsCache };
        private readonly IGuestShadowBackend _backend;
        private readonly Guid _hostBinding, _lease;
        private readonly int _thread;
        private readonly bool[] _restoreDispatched = new bool[Order.Length];
        private readonly GuestShadowRootReadback[] _roots = new GuestShadowRootReadback[Order.Length];
        private bool _claimed, _installAttempted, _captureAttempted, _fenceAttempted, _fenceRetained;
        private bool _fenceIntegrityLost, _referencesRetained, _originalsRestored, _installed;
        private bool _removeAttempted, _releaseAttempted, _busy;
        private GuestShadowStage _stage;
        private string _fault;
        private long _faultSerial;

        public GuestShadowTransaction(IGuestShadowBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _hostBinding = backend.HostBindingId; _lease = backend.LeaseId; _thread = backend.UnityThreadId;
            if (_hostBinding == Guid.Empty || _lease == Guid.Empty || _thread < 1)
                throw new ArgumentException("Shadow backend requires its own nonempty source lease and Unity thread.", nameof(backend));
        }

        public GuestShadowSnapshot Snapshot => new GuestShadowSnapshot
        {
            HostBindingId = _hostBinding, LeaseId = _lease, Stage = _stage, RootShadowInstalled = _installed,
            FenceRetained = _fenceRetained, FenceIntegrityLost = _fenceIntegrityLost,
            ReferencesRetained = _referencesRetained, OriginalsRestored = _originalsRestored,
            Fault = _fault, Roots = (GuestShadowRootReadback[])_roots.Clone()
        };

        public GuestShadowResult Install()
        {
            GuestShadowResult guard = Enter(); if (guard != null) return guard;
            try
            {
                if (_installAttempted) return Result(GuestShadowReason.Duplicate, "This lease has already attempted installation.");
                if (_fault != null) return Result(GuestShadowReason.Faulted, _fault);
                _installAttempted = true; _stage = GuestShadowStage.Installing;
                GuestShadowResult failure = Call(_backend.TryClaimLease, GuestShadowReason.LeaseAlreadyUsed, "Claim lease");
                if (failure != null) return FailInstall(failure);
                _claimed = true;
                failure = Binding(); if (failure != null) return FailInstall(failure);
                // The production source boundary is independent of the fence.
                // Reject unavailable entry before acquiring cleanup obligations;
                // this precheck alone never permits capture or native cloning.
                failure = Call(_backend.CanEnterBoundary, GuestShadowReason.BoundaryUnavailable, "Confirm source entry boundary");
                if (failure != null) return FailInstall(failure);
                _fenceAttempted = true; _fenceRetained = true;
                failure = Call(_backend.InstallOutputFence, GuestShadowReason.FenceUnavailable, "Install output fence");
                if (failure != null) { _fenceIntegrityLost = true; return FailInstall(failure); }
                failure = Fence(); if (failure != null) return FailInstall(failure);
                failure = Call(_backend.CanEnterBoundary, GuestShadowReason.BoundaryUnavailable, "Enter fenced boundary");
                if (failure != null) return FailInstall(failure);
                _captureAttempted = true; _referencesRetained = true;
                failure = Call(_backend.CaptureOriginal, GuestShadowReason.CaptureFailed, "Capture original");
                if (failure != null) return FailInstall(failure);
                failure = Binding(); if (failure != null) return FailInstall(failure);
                failure = Fence(); if (failure != null) return FailInstall(failure);
                failure = Call(_backend.CanEnterBoundary, GuestShadowReason.BoundaryUnavailable, "Confirm clone boundary");
                if (failure != null) return FailInstall(failure);
                failure = Call(_backend.PrepareDetached, GuestShadowReason.PrepareFailed, "Prepare detached roots");
                if (failure != null) return FailInstall(failure);
                for (int i = 0; i < Order.Length; i++)
                {
                    failure = Binding(); if (failure != null) return FailInstall(failure);
                    failure = Fence(); if (failure != null) return FailInstall(failure);
                    failure = Call(_backend.CanEnterBoundary, GuestShadowReason.BoundaryUnavailable, "Confirm root installation boundary");
                    if (failure != null) return FailInstall(failure);
                    failure = Read(i);
                    if (failure != null) return FailInstall(failure);
                    if (_roots[i] != GuestShadowRootReadback.Original)
                        return FailInstall(Latch(GuestShadowReason.ForeignRoot, "An installation root is no longer original."));
                    int index = i;
                    failure = Call(() => _backend.InstallRoot(Order[index]), GuestShadowReason.InstallFailed, "Install " + Order[i]);
                    if (failure != null) return FailInstall(failure);
                    failure = Read(i);
                    if (failure != null) return FailInstall(failure);
                    if (_roots[i] != GuestShadowRootReadback.Detached)
                        return FailInstall(Latch(GuestShadowReason.ValidationFailed, "Installed root readback did not match its detached object."));
                }
                failure = Binding(); if (failure != null) return FailInstall(failure);
                failure = Fence(); if (failure != null) return FailInstall(failure);
                failure = Call(_backend.ValidateRoots, GuestShadowReason.ValidationFailed, "Validate installed roots");
                if (failure != null) return FailInstall(failure);
                failure = Binding() ?? Fence(); if (failure != null) return FailInstall(failure);
                if (_fault != null) return FailInstall(Result(GuestShadowReason.Faulted, _fault));
                _installed = true; _stage = GuestShadowStage.ShadowInstalled;
                return Result(GuestShadowReason.None, "Detached roots installed; guest isolation remains unverified.");
            }
            finally { _busy = false; }
        }

        public GuestShadowResult ValidateActive()
        {
            GuestShadowResult guard = Enter(); if (guard != null) return guard;
            try
            {
                if (_stage != GuestShadowStage.ShadowInstalled || !_installed)
                    return Result(GuestShadowReason.InvalidStage, "No active shadow roots.");
                GuestShadowResult failure = Binding() ?? Fence();
                if (failure == null)
                {
                    for (int i = 0; i < Order.Length && failure == null; i++)
                    {
                        failure = Read(i);
                        if (failure == null && _roots[i] != GuestShadowRootReadback.Detached)
                            failure = Latch(GuestShadowReason.ValidationFailed, "Active root readback changed.");
                    }
                }
                failure ??= Call(_backend.ValidateRoots, GuestShadowReason.ValidationFailed, "Validate active roots");
                failure ??= Binding() ?? Fence();
                if (_fault != null && failure == null) failure = Result(GuestShadowReason.Faulted, _fault);
                if (failure != null) { Compensate(); return failure; }
                return Result(GuestShadowReason.None, "Roots still installed; no native isolation permission.");
            }
            finally { _busy = false; }
        }

        public GuestShadowResult RestoreAndRelease()
        {
            GuestShadowResult guard = Enter(); if (guard != null) return guard;
            try
            {
                if (_stage == GuestShadowStage.Released) return Result(GuestShadowReason.Duplicate, "Lease already released.");
                if (!_claimed || !_fenceAttempted)
                    return Result(GuestShadowReason.InvalidStage, "No claimed fenced transaction to restore.");
                GuestShadowResult failure = Compensate();
                if (failure != null) return failure;
                if (_fenceIntegrityLost) return Result(GuestShadowReason.FenceUnavailable, "Fence integrity was lost; references remain retained.");
                failure = Fence(); if (failure != null) return failure;
                failure = Call(_backend.HasQuiescentBoundary, GuestShadowReason.QuiescenceUnavailable, "Confirm quiescent boundary", false);
                if (failure != null) return failure;
                // Recheck after the boundary check; its callbacks cannot silently
                // change managers, roots, or the output fence before removal.
                failure = Binding(); if (failure != null) return failure;
                failure = ConfirmAllOriginal(); if (failure != null) return failure;
                failure = Fence(); if (failure != null) return failure;
                if (_removeAttempted) return Result(GuestShadowReason.FenceRemovalFailed, "Fence removal outcome is unknown; it will not be dispatched again.");
                _removeAttempted = true;
                failure = Call(_backend.RemoveOutputFence, GuestShadowReason.FenceRemovalFailed, "Remove output fence");
                if (failure != null) return failure;
                try
                {
                    if (_backend.FenceIsActive()) return Latch(GuestShadowReason.FenceRemovalFailed, "Output fence remained active after removal.");
                }
                catch (Exception error) { return ExceptionResult(GuestShadowReason.FenceRemovalFailed, "Confirm fence removal", error); }
                _fenceRetained = false;
                if (_releaseAttempted) return Result(GuestShadowReason.ReferenceReleaseFailed, "Reference release will not be dispatched again.");
                _releaseAttempted = true;
                failure = Call(_backend.ReleaseReferences, GuestShadowReason.ReferenceReleaseFailed, "Release original and detached references");
                if (failure != null) return failure;
                _referencesRetained = false; _stage = GuestShadowStage.Released;
                return Result(GuestShadowReason.None, "Original roots and managers confirmed; fenced cleanup released.");
            }
            finally { _busy = false; }
        }

        private GuestShadowResult Enter()
        {
            if (Environment.CurrentManagedThreadId != _thread) return Latch(GuestShadowReason.WrongThread, "Shadow transaction changed Unity thread.");
            if (_busy) return Latch(GuestShadowReason.Reentrant, "Shadow transaction was reentered.");
            _busy = true; return null;
        }

        private GuestShadowResult Binding()
        {
            try
            {
                long before = _faultSerial;
                if (_backend.HostBindingId != _hostBinding || _backend.LeaseId != _lease || _backend.UnityThreadId != _thread || !_backend.BindingIsCurrent())
                    return Latch(GuestShadowReason.BindingChanged, "Bound backend source or manager identity changed.");
                if (_faultSerial != before) return Result(GuestShadowReason.Faulted, "Binding callback invalidated the transaction.");
                return null;
            }
            catch (Exception error) { return ExceptionResult(GuestShadowReason.BindingChanged, "Confirm binding", error); }
        }

        private GuestShadowResult Fence()
        {
            try
            {
                long before = _faultSerial;
                if (!_backend.FenceIsHealthy() || !_backend.FenceIsActive())
                {
                    _fenceIntegrityLost = true;
                    return Latch(GuestShadowReason.FenceUnavailable, "Output fence lost health or active coverage.");
                }
                if (_faultSerial != before) return Result(GuestShadowReason.Faulted, "Fence callback invalidated the transaction.");
                return null;
            }
            catch (Exception error)
            { _fenceIntegrityLost = true; return ExceptionResult(GuestShadowReason.FenceUnavailable, "Confirm output fence", error); }
        }

        private GuestShadowResult FailInstall(GuestShadowResult failure)
        {
            _installed = false; _stage = GuestShadowStage.Failed;
            if (_claimed && _captureAttempted) Compensate();
            return failure;
        }

        private GuestShadowResult Compensate()
        {
            _installed = false; _originalsRestored = false; _stage = GuestShadowStage.Restoring;
            GuestShadowResult binding = Binding();
            if (binding != null) { _stage = GuestShadowStage.Failed; return binding; }
            GuestShadowResult firstFailure = null;
            // Restore dependencies in reverse order. Inspect all roots even if
            // a previous root is foreign, unknown, or its write throws.
            for (int i = Order.Length - 1; i >= 0; i--)
            {
                GuestShadowResult failure = Read(i);
                if (failure == null && (_roots[i] == GuestShadowRootReadback.Detached ||
                    Order[i] == GuestShadowRoot.IngredientsCache && _roots[i] == GuestShadowRootReadback.OwnedMixed))
                {
                    if (_restoreDispatched[i]) failure = Latch(GuestShadowReason.RestoreFailed, "A restore is unresolved and will not be dispatched again.");
                    else
                    {
                        failure = Binding();
                        if (failure == null)
                        {
                            _restoreDispatched[i] = true; int index = i;
                            GuestShadowResult write = Call(() => _backend.RestoreRoot(Order[index]), GuestShadowReason.RestoreFailed, "Restore " + Order[i]);
                            GuestShadowResult read = Read(i);
                            // A false/throw may have restored the field. Exact
                            // readback resolves it, but the failure stays latched.
                            failure = _roots[i] == GuestShadowRootReadback.Original && read == null ? null : read ?? write ??
                                Latch(GuestShadowReason.RestoreFailed, "Restored root did not read back original.");
                        }
                    }
                }
                else if (failure == null && _roots[i] != GuestShadowRootReadback.Original)
                    failure = Latch(GuestShadowReason.ForeignRoot, "Foreign root will not be overwritten during compensation.");
                firstFailure ??= failure;
            }
            GuestShadowResult confirmation = ConfirmAllOriginal();
            if (confirmation == null)
            { _originalsRestored = true; _stage = GuestShadowStage.RestoredFenced; return null; }
            _stage = GuestShadowStage.Failed;
            return firstFailure ?? confirmation;
        }

        private GuestShadowResult ConfirmAllOriginal()
        {
            GuestShadowResult failure = Binding(); if (failure != null) return failure;
            for (int i = 0; i < Order.Length; i++)
            {
                failure = Read(i); if (failure != null) return failure;
                if (_roots[i] != GuestShadowRootReadback.Original)
                    return Latch(GuestShadowReason.RestoreFailed, "Original root readback is incomplete.");
            }
            return Call(_backend.ConfirmOriginalManagersAndRoots, GuestShadowReason.RestoreFailed, "Confirm original managers and roots");
        }

        private GuestShadowResult Read(int index)
        {
            try
            {
                long before = _faultSerial;
                GuestShadowRootReadback value = _backend.ReadRoot(Order[index]);
                if (value != GuestShadowRootReadback.Original && value != GuestShadowRootReadback.Detached && value != GuestShadowRootReadback.Foreign &&
                    !(Order[index] == GuestShadowRoot.IngredientsCache && value == GuestShadowRootReadback.OwnedMixed))
                { _roots[index] = GuestShadowRootReadback.Unknown; return Latch(GuestShadowReason.ReadbackUnknown, "Root readback is unknown."); }
                _roots[index] = value;
                return _faultSerial == before ? null : Result(GuestShadowReason.Faulted, "Readback callback invalidated the transaction.");
            }
            catch (Exception error)
            { _roots[index] = GuestShadowRootReadback.Unknown; return ExceptionResult(GuestShadowReason.ReadbackUnknown, "Read " + Order[index], error); }
        }

        private GuestShadowResult Call(Func<bool> action, GuestShadowReason reason, string stage, bool latchFalse = true)
        {
            try
            {
                long before = _faultSerial;
                bool accepted = action();
                if (_faultSerial != before) return Result(GuestShadowReason.Faulted, "Backend callback invalidated the transaction.");
                if (accepted) return null;
                return latchFalse ? Latch(reason, stage + " was rejected.") : Result(reason, stage + " is unavailable; fence and references remain retained.");
            }
            catch (Exception error) { return ExceptionResult(reason, stage, error); }
        }

        private GuestShadowResult ExceptionResult(GuestShadowReason reason, string stage, Exception error) =>
            Latch(reason, stage + " threw " + error.GetType().Name + "; outcome is not inferred from the exception.");
        private GuestShadowResult Latch(GuestShadowReason reason, string message)
        {
            if (_faultSerial != long.MaxValue) _faultSerial++;
            _fault ??= message; _installed = false;
            if (_stage != GuestShadowStage.Released) _stage = GuestShadowStage.Failed;
            return Result(reason, message);
        }
        private static GuestShadowResult Result(GuestShadowReason reason, string message) => new GuestShadowResult(reason, message);
    }
}
