using System;
using System.Threading;
using DaveCoop.Core.Cargo;
using Il2CppInterop.Runtime;

namespace DaveCoop.Networking
{
    internal sealed partial class LootObservationCapture
    {
        public const int MaxReturnGradeReadsPerSample = 16;
        public const int MaxProcessReturnGradeReads = 65536;
        private static long _processReturnGradeReads;
        public long ProcessReturnGradeReads => Interlocked.Read(ref _processReturnGradeReads);

        private sealed class ReturnGradeReader
        {
            private readonly LootObservationCapture _capture;
            private int _reads;
            public ReturnGradeReader(LootObservationCapture capture) { _capture = capture; }
            public T Read<T>(Func<T> read)
            {
                _capture.CheckReadWindow();
                if (_reads >= MaxReturnGradeReadsPerSample || !ClaimReturnGradeRead())
                    throw new InvalidOperationException("Return grade read quota exhausted.");
                _reads++; // Before any direct-field/class-store/identity work.
                return _capture.Read(read);
            }
        }

        private static bool ClaimReturnGradeRead()
        {
            while (true)
            {
                long current = Interlocked.Read(ref _processReturnGradeReads);
                if (current >= MaxProcessReturnGradeReads) return false;
                if (Interlocked.CompareExchange(ref _processReturnGradeReads, current + 1, current) == current) return true;
            }
        }

        private LootReturnGradeContextCandidate FreezeReturnGradeContext(LootBox receiver, int? additive, out IntPtr receiverClass)
        {
            receiverClass = IntPtr.Zero;
            CheckReadWindow();
            if (!additive.HasValue) return LootReturnGradeContextCandidate.Unavailable(null, "Original additive argument is unavailable.");
            if (ReferenceEquals(receiver, null)) return LootReturnGradeContextCandidate.Unavailable(additive, "Original grade receiver is null.");
            var reader = new ReturnGradeReader(this);
            long pointer = reader.Read(() => receiver.Pointer.ToInt64());
            if (pointer == 0) throw new InvalidOperationException("Return grade receiver identity is unavailable.");
            IntPtr expected = reader.Read(() => Il2CppClassPointerStore<LootBox>.NativeClassPtr);
            IntPtr actual = reader.Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer)));
            receiverClass = actual;
            if (expected == IntPtr.Zero || actual != expected)
                return LootReturnGradeContextCandidate.Unavailable(additive, "Original grade receiver is outside the supported LootBox class profile.");
            var first = new LootReturnGradeFields(reader.Read(() => receiver.k_IngredientDefaultGrade),
                reader.Read(() => receiver.k_MinItemGrade), reader.Read(() => receiver.k_MaxItemGrade));
            var second = new LootReturnGradeFields(reader.Read(() => receiver.k_IngredientDefaultGrade),
                reader.Read(() => receiver.k_MinItemGrade), reader.Read(() => receiver.k_MaxItemGrade));
            if (reader.Read(() => receiver.Pointer.ToInt64()) != pointer ||
                reader.Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer))) != actual ||
                reader.Read(() => Il2CppClassPointerStore<LootBox>.NativeClassPtr) != expected)
                throw new InvalidOperationException("Return grade receiver changed during copying.");
            GC.KeepAlive(receiver);
            // Only original receiver fields and additive. ApplyFinalGrade's
            // collection is obtained from SaveSystem, not proved by this.
            // No GetGameSave/GetLootBox/getter/Apply business call is made.
            return LootReturnGradeContextCandidate.Freeze(additive.Value, first, second);
        }

        private void CheckReturnGradeReceiver(LootBox receiver, long originalPointer, IntPtr originalClass)
        {
            CheckReadWindow();
            if (ReferenceEquals(receiver, null))
            {
                if (originalPointer != 0 || originalClass != IntPtr.Zero) throw new InvalidOperationException("Original grade receiver changed.");
                return;
            }
            var reader = new ReturnGradeReader(this);
            long pointer = reader.Read(() => receiver.Pointer.ToInt64());
            if (pointer != originalPointer || pointer == 0 ||
                reader.Read(() => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer))) != originalClass)
                throw new InvalidOperationException("Original grade receiver identity changed.");
            GC.KeepAlive(receiver);
            // The prefix scalars remain a historical sample. No after/final
            // policy is calculated and no finalizer reads native objects.
        }
    }
}
