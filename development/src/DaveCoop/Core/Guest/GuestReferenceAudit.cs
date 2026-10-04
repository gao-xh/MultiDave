using System.Collections.Generic;

namespace DaveCoop.Core.Guest
{
    public enum GuestReferenceAuditReason { None, InvalidReference, OutOfOrder, BudgetExceeded, SharedReference }

    // Single-threaded comparison of references actually read by the native
    // adapter. It cannot prove the adapter walked every mutable field, that
    // objects are alive, or that a source remained stable. Never a permission.
    public sealed class GuestReferenceAudit
    {
        public const int MaxReferences = 4096;
        private readonly HashSet<long> _original = new HashSet<long>();
        private readonly HashSet<long> _detached = new HashSet<long>();
        private bool _detachedStarted;
        public GuestReferenceAuditReason Reason { get; private set; }
        public bool Failed => Reason != GuestReferenceAuditReason.None;
        public int OriginalReferencesObserved { get; private set; }
        public int DetachedReferencesObserved { get; private set; }
        public int OriginalUniqueReferences => _original.Count;
        public int DetachedUniqueReferences => _detached.Count;
        public bool KnownReferencesDisjoint => _detachedStarted && !Failed && DetachedReferencesObserved > 0;

        public bool AddOriginal(long reference)
        {
            if (Failed) return false;
            if (_detachedStarted) return Fail(GuestReferenceAuditReason.OutOfOrder);
            if (reference == 0) return Fail(GuestReferenceAuditReason.InvalidReference);
            if (OriginalReferencesObserved >= MaxReferences) return Fail(GuestReferenceAuditReason.BudgetExceeded);
            OriginalReferencesObserved++;
            _original.Add(reference);
            return true;
        }

        public bool BeginDetached()
        {
            if (Failed) return false;
            if (_detachedStarted || OriginalReferencesObserved == 0) return Fail(GuestReferenceAuditReason.OutOfOrder);
            _detachedStarted = true;
            return true;
        }

        public bool AddDetached(long reference)
        {
            if (Failed) return false;
            if (!_detachedStarted) return Fail(GuestReferenceAuditReason.OutOfOrder);
            if (reference == 0) return Fail(GuestReferenceAuditReason.InvalidReference);
            if (DetachedReferencesObserved >= MaxReferences) return Fail(GuestReferenceAuditReason.BudgetExceeded);
            DetachedReferencesObserved++;
            if (_original.Contains(reference)) return Fail(GuestReferenceAuditReason.SharedReference);
            _detached.Add(reference);
            return true;
        }

        private bool Fail(GuestReferenceAuditReason reason)
        {
            if (!Failed) Reason = reason;
            return false;
        }
    }
}
