using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using SaveSystemType = DR.Save.SaveSystem;

namespace DaveCoop.Networking
{
    internal enum SaveStartupMethod
    {
        SaveUtilAwake, GameAwake, UserOptionAwake, GameInitFactory, GameInitMove,
        GameLoadFactory, GameLoadMove, GameAfterFactory, GameAfterMove, GameLoadSaved,
        SaveInit, SaveInitFactory, SaveInitMove, LoadAll, LoadOnInit, LoadGame, Reload, CloudReset,
        SaveFolder, DemoFolder, SavePath, DemoPath, FailedPath, OldPath, FileNameSlot, FileNameData,
        GameCreate, GameOnLoad, PlayerCreate, PlayerOnLoad, PlayerLoad, PlayerSetLoaded,
        PhotoCreate, PhotoOnLoad, OptionCreate, OptionOnLoad
    }

    // Synchronous arguments only; Capture must never enqueue wrappers or args.
    internal sealed class SaveStartupTarget
    {
        public MethodInfo Original, Prefix, Postfix;
        public SaveStartupMethod Kind;
        public string Key;
    }

    internal sealed class SaveStartupHooks : IDisposable
    {
        private const string Owner = Plugin.Id + ".save-startup";
        private static SaveStartupHooks _active;
        private readonly Dictionary<MethodBase, SaveStartupTarget> _methods = new Dictionary<MethodBase, SaveStartupTarget>();
        private List<SaveStartupTarget> _targets;
        private Harmony _harmony;
        private SaveStartupCapture _capture;
        private int _accepting, _failed, _callbackErrors, _cleanupEntered;
        public bool CleanupVerified { get; private set; } = true;
        public bool Installed => _harmony != null && Volatile.Read(ref _failed) == 0;
        public int TargetCount => _targets?.Count ?? 0;
        public int CallbackErrors => Volatile.Read(ref _callbackErrors);
        public bool Failed => Volatile.Read(ref _failed) != 0;
        public bool NativeHookAbiVerified => false;

        // Signature/patch setup may initialize generated types/native metadata.
        // There is no attempt to invoke or observe an original static ctor.
        public void Enable(SaveStartupCapture capture)
        {
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (_harmony != null || Failed) throw new InvalidOperationException("Startup hooks cannot be restarted.");
            if (Interlocked.CompareExchange(ref _active, this, null) != null)
                throw new InvalidOperationException("Another startup observer is active.");
            _capture = capture;
            try
            {
                _targets = CreateTargets();
                _harmony = new Harmony(Owner); CleanupVerified = false;
                // Calls through already installed detours can be recorded while
                // later targets are installing. Coverage remains incomplete.
                Volatile.Write(ref _accepting, 1);
                foreach (SaveStartupTarget target in _targets)
                {
                    _harmony.Patch(target.Original, new HarmonyMethod(target.Prefix), new HarmonyMethod(target.Postfix),
                        finalizer: new HarmonyMethod(Callback(nameof(Finally))));
                    var info = Harmony.GetPatchInfo(target.Original);
                    if (info == null || !info.Owners.Contains(Owner)) throw new InvalidOperationException("Own startup registration is missing.");
                }
            }
            catch
            {
                Volatile.Write(ref _failed, 1); Volatile.Write(ref _accepting, 0);
                try { RemoveOwn(); } catch { }
                throw;
            }
        }

        private List<SaveStartupTarget> CreateTargets()
        {
            var targets = new List<SaveStartupTarget>();
            Type iterator = typeof(Il2CppSystem.Collections.IEnumerator);
            Add(targets, SaveStartupMethod.SaveUtilAwake, typeof(SaveUtil), "Awake", typeof(void));
            Add(targets, SaveStartupMethod.GameAwake, typeof(GameBase), "Awake_Impl", typeof(void));
            Add(targets, SaveStartupMethod.UserOptionAwake, typeof(DR.Save.SaveSystemUserOptionManager), "Awake", typeof(void));
            Add(targets, SaveStartupMethod.GameInitFactory, typeof(GameBase), "Init", iterator);
            Add(targets, SaveStartupMethod.GameInitMove, typeof(GameBase._Init_d__32), "MoveNext", typeof(bool));
            Add(targets, SaveStartupMethod.GameLoadFactory, typeof(GameBase), "LoadGameData", iterator);
            Add(targets, SaveStartupMethod.GameLoadMove, typeof(GameBase._LoadGameData_d__43), "MoveNext", typeof(bool));
            Add(targets, SaveStartupMethod.GameAfterFactory, typeof(GameBase), "InitAfterSaveSystem", iterator);
            Add(targets, SaveStartupMethod.GameAfterMove, typeof(GameBase._InitAfterSaveSystem_d__45), "MoveNext", typeof(bool));
            Add(targets, SaveStartupMethod.GameLoadSaved, typeof(GameBase), "LoadSavedData", typeof(void));
            Add(targets, SaveStartupMethod.SaveInit, typeof(SaveSystemType), "Init", typeof(void), typeof(Il2CppSystem.Action));
            Add(targets, SaveStartupMethod.SaveInitFactory, typeof(SaveSystemType), "InitSaveSystem", iterator, typeof(Il2CppSystem.Action));
            Add(targets, SaveStartupMethod.SaveInitMove, typeof(SaveSystemType._InitSaveSystem_d__42), "MoveNext", typeof(bool));
            Add(targets, SaveStartupMethod.LoadAll, typeof(SaveSystemType), "LoadAllData", typeof(void));
            Add(targets, SaveStartupMethod.LoadOnInit, typeof(SaveSystemType), "LoadGameOnInit", typeof(void));
            Add(targets, SaveStartupMethod.LoadGame, typeof(SaveSystemType), "LoadGame", typeof(void));
            Add(targets, SaveStartupMethod.Reload, typeof(SaveSystemType), "ReloadData", typeof(void));
            Add(targets, SaveStartupMethod.CloudReset, typeof(SaveSystemType), "ResetAfterCloudLoad", typeof(void));
            Add(targets, SaveStartupMethod.SaveFolder, typeof(SaveSystemType), "GetSaveFolder", typeof(string));
            Add(targets, SaveStartupMethod.DemoFolder, typeof(SaveSystemType), "GetDemoSaveFolder", typeof(string));
            Type data = typeof(DR.Save.SaveDataType), slot = typeof(DR.Save.SaveSlotType);
            Add(targets, SaveStartupMethod.SavePath, typeof(SaveSystemType), "GetSaveFilePath", typeof(string), data, typeof(int), slot);
            Add(targets, SaveStartupMethod.DemoPath, typeof(SaveSystemType), "GetDemoSaveFilePath", typeof(string), data, typeof(int), slot);
            Add(targets, SaveStartupMethod.FailedPath, typeof(SaveSystemType), "GetFailedSaveFilePath", typeof(string), data, typeof(int), slot);
            Add(targets, SaveStartupMethod.OldPath, typeof(SaveSystemType), "GetOldSaveFilePath", typeof(string), data, typeof(int), slot);
            Add(targets, SaveStartupMethod.FileNameSlot, typeof(SaveSystemType), "GetSaveFileName", typeof(string), slot);
            Add(targets, SaveStartupMethod.FileNameData, typeof(SaveSystemType), "GetSaveFileName", typeof(string), data, typeof(int), slot);
            Add(targets, SaveStartupMethod.GameCreate, typeof(DR.Save.SaveSystemGameDataManager), "CreateManagedData", typeof(void));
            Add(targets, SaveStartupMethod.GameOnLoad, typeof(DR.Save.SaveSystemGameDataManager), "OnLoadData", typeof(void));
            Add(targets, SaveStartupMethod.PlayerCreate, typeof(DR.Save.SaveSystemPlayerDataManager), "CreateManagedData", typeof(void));
            Add(targets, SaveStartupMethod.PlayerOnLoad, typeof(DR.Save.SaveSystemPlayerDataManager), "OnLoadData", typeof(void));
            Add(targets, SaveStartupMethod.PlayerLoad, typeof(DR.Save.SaveSystemPlayerDataManager), "LoadData", typeof(void));
            Add(targets, SaveStartupMethod.PlayerSetLoaded, typeof(DR.Save.SaveSystemPlayerDataManager), "SetLoadedData", typeof(void), typeof(DR.Save.SavePlayerData));
            Add(targets, SaveStartupMethod.PhotoCreate, typeof(DR.Save.SaveSystemPhotoDataManager), "CreateManagedData", typeof(void));
            Add(targets, SaveStartupMethod.PhotoOnLoad, typeof(DR.Save.SaveSystemPhotoDataManager), "OnLoadData", typeof(void));
            Add(targets, SaveStartupMethod.OptionCreate, typeof(DR.Save.SaveSystemUserOptionManager), "CreateManagedData", typeof(void));
            Add(targets, SaveStartupMethod.OptionOnLoad, typeof(DR.Save.SaveSystemUserOptionManager), "OnLoadData", typeof(void));
            return targets;
        }

        private void Add(List<SaveStartupTarget> targets, SaveStartupMethod kind, Type type, string name, Type result, params Type[] parameters)
        {
            MethodInfo original = type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, parameters, null);
            if (original == null || original.DeclaringType != type || original.IsStatic || original.IsGenericMethod || original.ReturnType != result)
                throw new InvalidOperationException("An exact startup declaration is unavailable.");
            var target = new SaveStartupTarget { Kind = kind, Original = original, Prefix = Callback(nameof(Before)),
                Postfix = Callback(result == typeof(string) ? nameof(PathAfter) : result == typeof(bool) ? nameof(BoolAfter) :
                    result == typeof(Il2CppSystem.Collections.IEnumerator) ? nameof(IteratorAfter) : nameof(After)),
                Key = type.FullName + "::" + name + "(" + string.Join(",", parameters.Select(p => p.FullName)) + ")" };
            _methods.Add(original, target); targets.Add(target);
        }
        private static MethodInfo Callback(string name) => typeof(SaveStartupHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("An own startup callback is missing.");

        private static void Before(MethodBase __originalMethod, Il2CppObjectBase __instance, object[] __args, out long __state)
        {
            __state = 0; SaveStartupHooks active = Volatile.Read(ref _active);
            if (active == null || Volatile.Read(ref active._accepting) == 0) return;
            try { __state = active._capture.Begin(active._methods[__originalMethod], __instance, __args); }
            catch { active.CallbackFailed(); }
        }
        private static void After(Il2CppObjectBase __instance, long __state) => End(__state, __instance);
        private static void BoolAfter(Il2CppObjectBase __instance, bool __result, long __state) => End(__state, __instance, originalBool: __result);
        private static void PathAfter(Il2CppObjectBase __instance, string __result, long __state) => End(__state, __instance, path: __result, hasPath: true);
        private static void IteratorAfter(Il2CppObjectBase __instance, Il2CppSystem.Collections.IEnumerator __result, long __state)
            => End(__state, __instance, iteratorPresent: !ReferenceEquals(__result, null));
        private static void End(long id, Il2CppObjectBase instance, bool? originalBool = null, string path = null, bool hasPath = false, bool iteratorPresent = false)
        {
            SaveStartupHooks active = Volatile.Read(ref _active);
            if (id == 0 || active == null || Volatile.Read(ref active._accepting) == 0) return;
            try { active._capture.Complete(id, instance, originalBool, path, hasPath, iteratorPresent); }
            catch { active.CallbackFailed(); }
        }
        // Void finalizer never suppresses/replaces the original exception.
        private static void Finally(Exception __exception, long __state)
        {
            SaveStartupHooks active = Volatile.Read(ref _active);
            if (__state == 0 || active == null || Volatile.Read(ref active._accepting) == 0) return;
            try { active._capture.FinalizeCall(__state, !ReferenceEquals(__exception, null)); }
            catch { active.CallbackFailed(); }
        }
        private void CallbackFailed()
        {
            Interlocked.Increment(ref _callbackErrors); Volatile.Write(ref _failed, 1); Volatile.Write(ref _accepting, 0);
            try { _capture?.CallbackFailed(); } catch { }
        }
        public void CheckHealthy()
        {
            if (Failed || _harmony == null || _targets == null) throw new InvalidOperationException("Startup hooks are unavailable.");
            foreach (SaveStartupTarget target in _targets)
            {
                var info = Harmony.GetPatchInfo(target.Original);
                if (info == null || !info.Owners.Contains(Owner))
                { Volatile.Write(ref _failed, 1); throw new InvalidOperationException("An own startup hook was lost."); }
            }
        }
        public void Dispose() { Volatile.Write(ref _accepting, 0); RemoveOwn(); }
        private void RemoveOwn()
        {
            if (_harmony != null)
            {
                if (Interlocked.CompareExchange(ref _cleanupEntered, 1, 0) != 0)
                    throw new InvalidOperationException("Own startup cleanup was already entered; its unknown result is not retried.");
                _harmony.UnpatchSelf();
                if (_targets != null && _targets.Any(t => Harmony.GetPatchInfo(t.Original)?.Owners.Contains(Owner) == true))
                { CleanupVerified = false; throw new InvalidOperationException("Own startup cleanup could not be verified."); }
                _harmony = null;
            }
            CleanupVerified = true;
            Interlocked.CompareExchange(ref _active, null, this);
            _capture = null;
        }
    }
}
