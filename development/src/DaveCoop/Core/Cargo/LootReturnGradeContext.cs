namespace DaveCoop.Core.Cargo
{
    // Original instance-field scalars only. Their names do not establish the
    // native rule, its collection, or whether it applies to an employee.
    public sealed class LootReturnGradeFields
    {
        public int IngredientDefaultGrade { get; }
        public int MinItemGrade { get; }
        public int MaxItemGrade { get; }

        public LootReturnGradeFields(int ingredientDefaultGrade, int minItemGrade, int maxItemGrade)
        {
            IngredientDefaultGrade = ingredientDefaultGrade;
            MinItemGrade = minItemGrade;
            MaxItemGrade = maxItemGrade;
        }
    }

    // Agreement between two copied reads is not an atomic native snapshot or a
    // conversion policy. No final grade or policy fingerprint is computed here.
    public sealed class LootReturnGradeContextCandidate
    {
        public int? AdditiveGrade { get; }
        public LootReturnGradeFields FirstRead { get; }
        public LootReturnGradeFields RepeatedRead { get; }
        public bool SamplesMatch { get; }
        public bool CandidatesAvailable => AdditiveGrade.HasValue && SamplesMatch;
        public bool BoundsOrdered => SamplesMatch && FirstRead.MinItemGrade <= FirstRead.MaxItemGrade;
        public string UnavailableReason { get; }
        public bool ObservationOnly => true;
        public bool NativeFieldAbiVerified => false;
        public bool FinalGradeVerified => false;
        public bool ReceiverCollectionBound => false;
        public bool EmployeePolicyApplicable => false;
        public bool ReturnConversionVerified => false;
        public bool Permission => false;

        private LootReturnGradeContextCandidate(int? additiveGrade, LootReturnGradeFields first,
            LootReturnGradeFields repeated, bool samplesMatch, string reason)
        {
            AdditiveGrade = additiveGrade;
            FirstRead = Copy(first);
            RepeatedRead = Copy(repeated);
            SamplesMatch = samplesMatch;
            UnavailableReason = reason;
        }

        public static LootReturnGradeContextCandidate Freeze(int additive,
            LootReturnGradeFields first, LootReturnGradeFields second)
        {
            if (first == null)
                return new LootReturnGradeContextCandidate(additive, first, second, false, "First field sample is unavailable.");
            if (second == null)
                return new LootReturnGradeContextCandidate(additive, first, second, false, "Repeated field sample is unavailable.");
            bool same = first.IngredientDefaultGrade == second.IngredientDefaultGrade &&
                first.MinItemGrade == second.MinItemGrade && first.MaxItemGrade == second.MaxItemGrade;
            return new LootReturnGradeContextCandidate(additive, first, second, same,
                same ? null : "Original field samples differ.");
        }

        public static LootReturnGradeContextCandidate Unavailable(int? additive, string reason)
            => new LootReturnGradeContextCandidate(additive, null, null, false,
                string.IsNullOrEmpty(reason) ? "Return grade field samples are unavailable." : reason);

        private static LootReturnGradeFields Copy(LootReturnGradeFields fields)
            => fields == null ? null : new LootReturnGradeFields(fields.IngredientDefaultGrade, fields.MinItemGrade, fields.MaxItemGrade);
    }
}
