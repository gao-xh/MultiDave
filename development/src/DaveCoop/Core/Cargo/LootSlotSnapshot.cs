namespace DaveCoop.Core.Cargo
{
    // Scalars copied by an adapter, not a native struct, wrapper or pointer.
    public sealed class LootObscuredIntSnapshot
    {
        public int CurrentCryptoKey { get; }
        public int HiddenValue { get; }
        public bool Inited { get; }
        public int FakeValue { get; }
        public bool FakeValueActive { get; }

        public LootObscuredIntSnapshot(int currentCryptoKey, int hiddenValue, bool inited,
            int fakeValue, bool fakeValueActive)
        {
            CurrentCryptoKey = currentCryptoKey; HiddenValue = hiddenValue; Inited = inited;
            FakeValue = fakeValue; FakeValueActive = fakeValueActive;
        }
    }

    public enum LootObscuredIntUnavailableReason
    {
        None = 0,
        MissingSnapshot = 1,
        NotInitialized = 2,
        ZeroCryptoKey = 3,
        FakeValueMismatch = 4
    }

    // A decoded scalar is still only a candidate. It cannot authenticate a slot
    // sample, its quality, a bag change, member ownership or a capture receipt.
    public sealed class LootObscuredIntCandidate
    {
        public int? Value { get; }
        public bool Available => Value.HasValue;
        public LootObscuredIntUnavailableReason UnavailableReason { get; }
        public bool ObservationOnly => true;
        public bool NativeFieldAbiVerified => false;
        public bool FinalGradeVerified => false;
        public bool BagDelta => false;
        public bool FullYield => false;
        public bool Permission => false;

        private LootObscuredIntCandidate(int? value, LootObscuredIntUnavailableReason reason)
        { Value = value; UnavailableReason = reason; }

        internal static LootObscuredIntCandidate Decoded(int value)
            => new LootObscuredIntCandidate(value, LootObscuredIntUnavailableReason.None);
        internal static LootObscuredIntCandidate Unavailable(LootObscuredIntUnavailableReason reason)
            => new LootObscuredIntCandidate(null, reason);
    }

    public static class LootSlotSnapshot
    {
        // This restricted branch mirrors the independently inspected 32-bit
        // arithmetic only. Key-zero and uninitialized native branches can read
        // static state or initialize fields, so they deliberately stay unavailable.
        public static LootObscuredIntCandidate DecodeInt(LootObscuredIntSnapshot snapshot)
        {
            if (snapshot == null)
                return LootObscuredIntCandidate.Unavailable(LootObscuredIntUnavailableReason.MissingSnapshot);
            if (!snapshot.Inited)
                return LootObscuredIntCandidate.Unavailable(LootObscuredIntUnavailableReason.NotInitialized);
            if (snapshot.CurrentCryptoKey == 0)
                return LootObscuredIntCandidate.Unavailable(LootObscuredIntUnavailableReason.ZeroCryptoKey);
            int value = unchecked(snapshot.HiddenValue ^ snapshot.CurrentCryptoKey);
            if (snapshot.FakeValueActive && snapshot.FakeValue != value)
                return LootObscuredIntCandidate.Unavailable(LootObscuredIntUnavailableReason.FakeValueMismatch);
            return LootObscuredIntCandidate.Decoded(value);
        }
    }
}
