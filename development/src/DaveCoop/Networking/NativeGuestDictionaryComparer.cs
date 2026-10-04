using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DaveCoop.Networking
{
    // Seven exact closed declarations with no declared instance fields.
    // This does not prove native initialization, globals or key semantics ABI.
    internal static class NativeGuestDictionaryComparer
    {
        internal enum Kind
        {
            None, GenericInt, ObjectInt, GenericString, ObjectString,
            GenericIngame, ObjectIngame, EnumIngame
        }

        internal sealed class Stamp
        {
            public Type KeyType { get; }
            public Kind ClassKind { get; }
            public IntPtr NativeClass { get; }
            public long Pointer { get; }
            public Il2CppObjectBase Reference { get; }
            public long DefaultComparerPointer { get; }
            // Observed static resource, not an owned detached graph node.
            public Il2CppObjectBase DefaultReference { get; }
            internal Stamp(Type keyType, Kind kind, IntPtr nativeClass, long pointer, Il2CppObjectBase reference,
                long defaultPointer, Il2CppObjectBase defaultReference)
            { KeyType = keyType; ClassKind = kind; NativeClass = nativeClass; Pointer = pointer; Reference = reference;
                DefaultComparerPointer = defaultPointer; DefaultReference = defaultReference; }
        }

        internal sealed class Failure : InvalidOperationException
        { public Failure(string reason) : base(reason) { } }

        public static bool NativeCloneAbiVerified => false;
        public static bool PartialConstructorAllocationRetentionVerified => false;
        public static bool NativePermission => false;

        public static Stamp Capture<TKey>(Il2CppSystem.Collections.Generic.IEqualityComparer<TKey> comparer, Action freshWindow)
        {
            if (typeof(TKey) != typeof(int) && typeof(TKey) != typeof(string) && typeof(TKey) != typeof(InGameSaveType))
                Fail("Missing proof for an unsupported dictionary key type.");
            var defaultReference = Read(freshWindow, () => Il2CppSystem.Collections.Generic.EqualityComparer<TKey>.defaultComparer);
            long defaultPointer = Read(freshWindow, () => defaultReference == null ? 0 : Pointer(defaultReference));
            if (comparer == null)
            {
                RequireDefaultUnchanged<TKey>(defaultPointer, freshWindow);
                return new Stamp(typeof(TKey), Kind.None, IntPtr.Zero, 0, null, defaultPointer, defaultReference);
            }
            long pointer = Read(freshWindow, () => Pointer(comparer));
            IntPtr actual = Read(freshWindow, () => IL2CPP.il2cpp_object_get_class(new IntPtr(pointer)));
            Kind match = Kind.None;
            if (typeof(TKey) == typeof(int))
            {
                Match(Kind.GenericInt, actual, freshWindow, ref match);
                Match(Kind.ObjectInt, actual, freshWindow, ref match);
            }
            else if (typeof(TKey) == typeof(string))
            {
                Match(Kind.GenericString, actual, freshWindow, ref match);
                Match(Kind.ObjectString, actual, freshWindow, ref match);
            }
            else
            {
                Match(Kind.EnumIngame, actual, freshWindow, ref match);
                Match(Kind.GenericIngame, actual, freshWindow, ref match);
                Match(Kind.ObjectIngame, actual, freshWindow, ref match);
            }
            if (match == Kind.None || actual == IntPtr.Zero || ReadClass(match, freshWindow) != actual ||
                Read(freshWindow, () => IL2CPP.il2cpp_object_get_class(new IntPtr(Pointer(comparer)))) != actual)
                Fail("Missing proof for an exact dictionary comparer class.");
            RequireDefaultUnchanged<TKey>(defaultPointer, freshWindow);
            return new Stamp(typeof(TKey), match, actual, pointer, comparer, defaultPointer, defaultReference);
        }

        public static void RequireCloneable(Stamp source)
        {
            if (source == null || source.ClassKind == Kind.None)
                Fail("A null dictionary comparer cannot be replaced by a guessed default.");
        }

        public static void RequireSameSemantics(Stamp original, Stamp detached)
        {
            RequireCloneable(original); RequireCloneable(detached);
            if (original.KeyType != detached.KeyType || original.ClassKind != detached.ClassKind || original.NativeClass != detached.NativeClass ||
                original.DefaultComparerPointer != detached.DefaultComparerPointer)
                Fail("Original and detached dictionary comparer classes differ.");
        }

        public static void RequireSameIdentity(Stamp before, Stamp after)
        {
            if (before == null || after == null || before.KeyType != after.KeyType || before.ClassKind != after.ClassKind ||
                before.NativeClass != after.NativeClass || before.Pointer != after.Pointer || before.DefaultComparerPointer != after.DefaultComparerPointer)
                Fail("A dictionary comparer binding changed during capture.");
        }

        public static Il2CppSystem.Collections.Generic.IEqualityComparer<TKey> Copy<TKey>(
            Stamp source, Action freshWindow, Action<Il2CppObjectBase> retain)
        {
            RequireCloneable(source);
            if (source.KeyType != typeof(TKey) || retain == null) Fail("Dictionary comparer copy is not bound to its key type and keeper.");
            RequireDefaultUnchanged<TKey>(source.DefaultComparerPointer, freshWindow);
            IntPtr klass = ReadClass(source.ClassKind, freshWindow);
            if (klass == IntPtr.Zero || klass != source.NativeClass) Fail("The captured dictionary comparer class is unavailable.");
            RequireDefaultUnchanged<TKey>(source.DefaultComparerPointer, freshWindow);
            Il2CppObjectBase owned = null;
            freshWindow();
            IntPtr pointer = IL2CPP.il2cpp_object_new(klass);
            if (pointer == IntPtr.Zero) Fail("Dictionary comparer allocation returned no object.");
            // Keep the allocation before the post-call guard. Only framework
            // wrapping/retention occurs before any class read or business call.
            owned = Wrap(source.ClassKind, pointer);
            retain(owned);
            freshWindow();
            var result = Read(freshWindow, () => new Il2CppSystem.Collections.Generic.IEqualityComparer<TKey>(new IntPtr(Pointer(owned))));
            Stamp copied = Capture(result, freshWindow);
            RequireSameSemantics(source, copied);
            if (copied.Pointer == source.Pointer) Fail("Dictionary comparer copy aliases its source.");
            freshWindow();
            return result;
        }

        private static void Match(Kind kind, IntPtr actual, Action window, ref Kind match)
        {
            if (match != Kind.None) return;
            IntPtr candidate = ReadClass(kind, window);
            if (candidate == IntPtr.Zero || candidate != actual) return;
            match = kind;
        }

        private static void RequireDefaultUnchanged<TKey>(long expected, Action window)
        {
            var value = Read(window, () => Il2CppSystem.Collections.Generic.EqualityComparer<TKey>.defaultComparer);
            if (Read(window, () => value == null ? 0 : Pointer(value)) != expected)
                Fail("The observed dictionary default comparer resource changed.");
        }

        private static IntPtr ReadClass(Kind kind, Action window)
        {
            switch (kind)
            {
                case Kind.GenericInt: return Read(window, () => Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.GenericEqualityComparer<int>>.NativeClassPtr);
                case Kind.ObjectInt: return Read(window, () => Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.ObjectEqualityComparer<int>>.NativeClassPtr);
                case Kind.GenericString: return Read(window, () => Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.GenericEqualityComparer<string>>.NativeClassPtr);
                case Kind.ObjectString: return Read(window, () => Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.ObjectEqualityComparer<string>>.NativeClassPtr);
                case Kind.GenericIngame: return Read(window, () => Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.GenericEqualityComparer<InGameSaveType>>.NativeClassPtr);
                case Kind.ObjectIngame: return Read(window, () => Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.ObjectEqualityComparer<InGameSaveType>>.NativeClassPtr);
                case Kind.EnumIngame: return Read(window, () => Il2CppClassPointerStore<Il2CppSystem.Collections.Generic.EnumEqualityComparer<InGameSaveType>>.NativeClassPtr);
                default: Fail("An unsupported dictionary comparer kind cannot be initialized."); return IntPtr.Zero;
            }
        }

        private static Il2CppObjectBase Wrap(Kind kind, IntPtr pointer)
        {
            switch (kind)
            {
                case Kind.GenericInt: return new Il2CppSystem.Collections.Generic.GenericEqualityComparer<int>(pointer);
                case Kind.ObjectInt: return new Il2CppSystem.Collections.Generic.ObjectEqualityComparer<int>(pointer);
                case Kind.GenericString: return new Il2CppSystem.Collections.Generic.GenericEqualityComparer<string>(pointer);
                case Kind.ObjectString: return new Il2CppSystem.Collections.Generic.ObjectEqualityComparer<string>(pointer);
                case Kind.GenericIngame: return new Il2CppSystem.Collections.Generic.GenericEqualityComparer<InGameSaveType>(pointer);
                case Kind.ObjectIngame: return new Il2CppSystem.Collections.Generic.ObjectEqualityComparer<InGameSaveType>(pointer);
                case Kind.EnumIngame: return new Il2CppSystem.Collections.Generic.EnumEqualityComparer<InGameSaveType>(pointer);
                default: Fail("An unsupported dictionary comparer cannot be wrapped."); return null;
            }
        }

        private static T Read<T>(Action window, Func<T> read)
        { window(); T value = read(); window(); return value; }
        private static long Pointer(Il2CppObjectBase value)
        { if (value == null || value.Pointer == IntPtr.Zero) Fail("A required dictionary comparer reference is missing."); return value.Pointer.ToInt64(); }
        private static void Fail(string reason) => throw new Failure(reason);
    }
}
