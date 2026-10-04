using System;
using DaveCoop.Core.Guest;

internal static class GuestReferenceAuditTests
{
    internal static void DistinctRootsCannotHideSharedChildren()
    {
        var audit = new GuestReferenceAudit();
        Assert(audit.AddOriginal(100) && audit.AddOriginal(101) && audit.AddOriginal(150) && audit.BeginDetached(), "original graph was rejected");
        Assert(audit.AddDetached(200) && audit.AddDetached(201) && audit.KnownReferencesDisjoint, "distinct observed roots were rejected");
        Assert(!audit.AddDetached(101) && audit.Failed && audit.Reason == GuestReferenceAuditReason.SharedReference && !audit.KnownReferencesDisjoint,
            "a cloned root with a shared original child passed the reference audit");
        Assert(!audit.AddDetached(300) && !audit.AddOriginal(500) && !audit.BeginDetached() && audit.Reason == GuestReferenceAuditReason.SharedReference,
            "adding more disjoint nodes erased the shared-child failure");
        var crossBranch = new GuestReferenceAudit();
        Assert(crossBranch.AddOriginal(10) && crossBranch.AddOriginal(20) && crossBranch.BeginDetached(), "cross-branch source failed");
        Assert(crossBranch.AddDetached(30) && !crossBranch.AddDetached(20), "a shadow field reused another original branch");
    }

    internal static void SharingWithinOneSideIsAllowed()
    {
        var audit = new GuestReferenceAudit();
        Assert(audit.AddOriginal(100) && audit.AddOriginal(101) && audit.AddOriginal(101) && audit.BeginDetached(), "original cache alias was rejected");
        Assert(!audit.KnownReferencesDisjoint, "an unread detached graph was accepted");
        Assert(audit.AddDetached(200) && audit.AddDetached(201) && audit.AddDetached(201) && audit.KnownReferencesDisjoint,
            "a shadow player and its runtime cache may share their own detached container");
        Assert(audit.OriginalUniqueReferences == 2 && audit.DetachedUniqueReferences == 2 &&
            audit.OriginalReferencesObserved == 3 && audit.DetachedReferencesObserved == 3,
            "unique references or total traversal work was counted incorrectly");
        var signedPointers = new GuestReferenceAudit();
        Assert(signedPointers.AddOriginal(long.MinValue) && signedPointers.BeginDetached() && signedPointers.AddDetached(-1),
            "nonzero pointer bit patterns were incorrectly treated as numeric identities");
    }

    internal static void IncompleteOrExcessiveTraversalCannotPass()
    {
        var empty = new GuestReferenceAudit();
        Assert(!empty.KnownReferencesDisjoint && !empty.BeginDetached() && !empty.KnownReferencesDisjoint, "empty traversal passed");
        var missingOriginal = new GuestReferenceAudit();
        Assert(!missingOriginal.AddDetached(2) && missingOriginal.Reason == GuestReferenceAuditReason.OutOfOrder, "detached read preceded original collection");
        var zero = new GuestReferenceAudit();
        Assert(!zero.AddOriginal(0) && zero.Reason == GuestReferenceAuditReason.InvalidReference, "missing original reference passed");
        var missingShadow = new GuestReferenceAudit();
        Assert(missingShadow.AddOriginal(1) && missingShadow.BeginDetached() && !missingShadow.AddDetached(0) && !missingShadow.KnownReferencesDisjoint,
            "null shadow reference was silently ignored");
        var lateSource = new GuestReferenceAudit();
        Assert(lateSource.AddOriginal(1) && lateSource.BeginDetached() && lateSource.AddDetached(2) && !lateSource.AddOriginal(2) && !lateSource.KnownReferencesDisjoint,
            "a newly read original alias after the detached pass was ignored");
        var twice = new GuestReferenceAudit();
        Assert(twice.AddOriginal(1) && twice.BeginDetached() && !twice.BeginDetached(), "a second traversal reused the audit");
        var sourceBudget = new GuestReferenceAudit();
        for (int i = 0; i < GuestReferenceAudit.MaxReferences; i++) Assert(sourceBudget.AddOriginal(1), "exact source work limit failed");
        Assert(!sourceBudget.AddOriginal(1) && sourceBudget.Reason == GuestReferenceAuditReason.BudgetExceeded, "duplicate aliases bypassed original work bound");
        var shadowBudget = new GuestReferenceAudit();
        Assert(shadowBudget.AddOriginal(1) && shadowBudget.BeginDetached(), "shadow budget source failed");
        for (int i = 0; i < GuestReferenceAudit.MaxReferences; i++) Assert(shadowBudget.AddDetached(2), "exact detached work limit failed");
        Assert(shadowBudget.KnownReferencesDisjoint && !shadowBudget.AddDetached(2) && !shadowBudget.KnownReferencesDisjoint &&
            shadowBudget.Reason == GuestReferenceAuditReason.BudgetExceeded, "duplicate aliases bypassed detached work bound");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
