using System;
using DaveCoop.Core.Cargo;

internal static class LootSlotSnapshotTests
{
    internal static void SignedBoundaryVectorsRemainBitExact()
    {
        // Literal pairs were fixed independently; expected values are not
        // derived by repeating the decoder's expression in the fixture.
        var vectors = new (int Key, int Hidden, int Expected)[]
        {
            (305419896, 305419896, 0),
            (305419896, 305419897, 1),
            (305419896, -305419897, -1),
            (1, -2147483647, int.MinValue),
            (1, 2147483646, int.MaxValue),
            (int.MinValue, 0, int.MinValue),
            (int.MinValue, -1, int.MaxValue),
            (-1, 0, -1)
        };
        foreach (var vector in vectors)
        {
            var snapshot = new LootObscuredIntSnapshot(vector.Key, vector.Hidden, true, vector.Expected, true);
            LootObscuredIntCandidate result = LootSlotSnapshot.DecodeInt(snapshot);
            Assert(result.Available && result.Value == vector.Expected &&
                result.UnavailableReason == LootObscuredIntUnavailableReason.None,
                "signed boundary or high-bit scalar did not retain its exact 32-bit value");
            Assert(snapshot.CurrentCryptoKey == vector.Key && snapshot.HiddenValue == vector.Hidden && snapshot.Inited &&
                snapshot.FakeValue == vector.Expected && snapshot.FakeValueActive,
                "candidate decoding changed one of the copied scalar inputs");
            NoPermission(result);
        }
    }

    internal static void InactiveFakeValueDoesNotRejectOrRepairTheSnapshot()
    {
        var snapshot = new LootObscuredIntSnapshot(305419896, 305419903, true, int.MinValue, false);
        LootObscuredIntCandidate result = LootSlotSnapshot.DecodeInt(snapshot);
        Assert(result.Available && result.Value == 7 && result.UnavailableReason == LootObscuredIntUnavailableReason.None,
            "inactive inconsistent fake value was used as a tamper check or as the decoded value");
        Assert(snapshot.CurrentCryptoKey == 305419896 && snapshot.HiddenValue == 305419903 && snapshot.Inited &&
            snapshot.FakeValue == int.MinValue && !snapshot.FakeValueActive,
            "decoder repaired or activated the copied fake-value fields");
        NoPermission(result);
    }

    internal static void MissingInitializationAndZeroKeyNeverInventAValue()
    {
        LootObscuredIntCandidate absent = LootSlotSnapshot.DecodeInt(null);
        Unavailable(absent, LootObscuredIntUnavailableReason.MissingSnapshot);
        var uninitialized = new LootObscuredIntSnapshot(305419896, 305419896, false, 0, true);
        Unavailable(LootSlotSnapshot.DecodeInt(uninitialized), LootObscuredIntUnavailableReason.NotInitialized);
        Assert(!uninitialized.Inited && uninitialized.CurrentCryptoKey == 305419896 &&
            uninitialized.HiddenValue == 305419896 && uninitialized.FakeValue == 0 && uninitialized.FakeValueActive,
            "uninitialized snapshot was fabricated as initialized zero");
        var zeroKey = new LootObscuredIntSnapshot(0, 777, true, 777, false);
        Unavailable(LootSlotSnapshot.DecodeInt(zeroKey), LootObscuredIntUnavailableReason.ZeroCryptoKey);
        Assert(zeroKey.CurrentCryptoKey == 0 && zeroKey.HiddenValue == 777 && zeroKey.Inited &&
            zeroKey.FakeValue == 777 && !zeroKey.FakeValueActive,
            "zero-key snapshot was filled from a guessed static/default key");
        Unavailable(LootSlotSnapshot.DecodeInt(new LootObscuredIntSnapshot(0, 0, false, -1, true)),
            LootObscuredIntUnavailableReason.NotInitialized);
    }

    internal static void TamperAndLaterGradeCandidatesCannotGrantCargoProof()
    {
        var tampered = new LootObscuredIntSnapshot(777, 778, true, 4, true);
        Unavailable(LootSlotSnapshot.DecodeInt(tampered), LootObscuredIntUnavailableReason.FakeValueMismatch);
        Assert(tampered.CurrentCryptoKey == 777 && tampered.HiddenValue == 778 && tampered.Inited &&
            tampered.FakeValue == 4 && tampered.FakeValueActive, "tamper rejection overwrote the original fake value");

        var supplied = new LootObscuredIntSnapshot(777, 778, true, 3, true);
        LootObscuredIntCandidate held = LootSlotSnapshot.DecodeInt(supplied);
        supplied = new LootObscuredIntSnapshot(777, 781, true, 4, true);
        LootObscuredIntCandidate later = LootSlotSnapshot.DecodeInt(supplied);
        Assert(held.Available && held.Value == 3 && later.Available && later.Value == 4 &&
            !ReferenceEquals(held, later), "later copied grade data rebound a held candidate");
        NoPermission(held); NoPermission(later);
        // A zero scalar is an available candidate, unlike unavailable nullable
        // results. Neither result grants validity to any slot or capture.
        LootObscuredIntCandidate zero = LootSlotSnapshot.DecodeInt(new LootObscuredIntSnapshot(-1, -1, true, 0, true));
        Assert(zero.Available && zero.Value == 0, "available zero was confused with missing data");
        NoPermission(zero);
    }

    private static void Unavailable(LootObscuredIntCandidate result, LootObscuredIntUnavailableReason reason)
    {
        Assert(!result.Available && result.Value == null && result.UnavailableReason == reason,
            "unavailable input produced a guessed scalar or the wrong fixed reason");
        NoPermission(result);
    }
    private static void NoPermission(LootObscuredIntCandidate result)
    {
        Assert(result.ObservationOnly && !result.NativeFieldAbiVerified && !result.FinalGradeVerified &&
            !result.BagDelta && !result.FullYield && !result.Permission,
            "pure scalar decoding granted native, quality, bag, yield or capture permission");
    }
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
