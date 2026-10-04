using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using BepInEx.Logging;
using DaveCoop.Core.Guest;
using Il2CppInterop.Runtime.InteropTypes;
using SaveSystemType = DR.Save.SaveSystem;

namespace DaveCoop.Networking
{
    internal sealed class SaveStartupCapture : IDisposable
    {
        public const int MaxQueued = SaveStartupTrace.MaxEvents;
        public const int MaxDrainPerUpdate = 16;
        public const int MaxPathCharacters = 4096;
        public const int MaxRunPathCharacters = 256 * 1024;
        public const int MaxNativeOrdinals = 256;
        public const int MaxStateLogs = 128;
        // Start is process-single-use, so this Run budget cannot be reset.
        public const int MaxProcessEvents = SaveStartupTrace.MaxRunEvents;
        private static int _processStarted;
        private readonly object _gate = new object();
        private readonly ManualLogSource _logger;
        private readonly SaveStartupTrace _trace;
        private readonly SaveStartupHooks _hooks = new SaveStartupHooks();
        private readonly Queue<SaveStartupObservation> _pending = new Queue<SaveStartupObservation>();
        private readonly Dictionary<long, Prefix> _prefixes = new Dictionary<long, Prefix>();
        private readonly Dictionary<long, SaveStartupObservation> _details = new Dictionary<long, SaveStartupObservation>();
        private readonly Dictionary<long, long> _ordinals = new Dictionary<long, long>();
        private bool _freezing, _stopped, _firstUpdate, _cleanupWarned, _terminalStateLogged;
        private long _dropped, _readErrors, _unexpectedThreads, _logged, _pathLimitFailures;
        private int _pathCharacters, _stateLogs;
        private double _nextState;
        private sealed class Prefix
        { public SaveStartupTarget Target; public SaveStartupObservation Scalars; public long? Instance, Owner; }
        public string Status { get; private set; } = "Save startup observer: starting, incomplete evidence";
        public static double MonotonicNow => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

        private SaveStartupCapture(ManualLogSource logger, int pluginLoadThread, double pluginLoadAt)
        { _logger = logger ?? throw new ArgumentNullException(nameof(logger)); _trace = new SaveStartupTrace(pluginLoadThread, pluginLoadAt); }

        // Called only for config=true. No generated type is touched on the off path.
        public static SaveStartupCapture Start(ManualLogSource logger, int pluginLoadThread, double pluginLoadAt)
        {
            var capture = new SaveStartupCapture(logger, pluginLoadThread, pluginLoadAt);
            if (Interlocked.CompareExchange(ref _processStarted, 1, 0) != 0)
            { capture.CallbackFailed(); capture.Status = "Save startup observer: process restart required"; return capture; }
            capture._logger.LogInfo("DAVECOOP_SAVE_STARTUP_SETUP_BEGIN: " + JsonSerializer.Serialize(new
            {
                capture._trace.RunId, PluginLoadThread = pluginLoadThread, PluginLoadAt = pluginLoadAt,
                SetupStartedAt = MonotonicNow, SetupMayInitializeNativeTypes = true,
                PreInstallationAccessExcluded = false, InstallationCoverageVerified = false,
                ObservationOnly = true, StartupCompleteness = false, FirstLoadOrderVerified = false,
                GuestStateIsolated = false, NativePermission = false
            }));
            try
            {
                capture._hooks.Enable(capture);
                lock (capture._gate) { capture._trace.MarkHooksInstalled(MonotonicNow); capture.FlushCore(); }
                capture.Status = "Save startup observer: read-only; early coverage unknown";
            }
            catch (Exception error)
            {
                capture.CallbackFailed();
                capture._logger.LogWarning("DAVECOOP_SAVE_STARTUP_SETUP_FAILED: " + error.GetType().Name + "; no exception text recorded.");
                capture.Stop("Startup hook installation failed.");
            }
            return capture;
        }

        public long Begin(SaveStartupTarget target, Il2CppObjectBase instance, object[] arguments)
        {
            lock (_gate)
            {
                if (_stopped) return 0;
                if (_freezing) { CallbackFailed(); throw new InvalidOperationException("Startup value capture reentered."); }
                if (!_trace.BeginCall(target.Key, MonotonicNow, out long id)) { FlushCore(); return 0; }
                long sequence = _trace.LastSequence;
                var value = new SaveStartupObservation { InstanceWrapperPresent = !ReferenceEquals(instance, null) };
                CopyArguments(target.Kind, arguments, value);
                var prefix = new Prefix { Target = target, Scalars = value };
                _prefixes.Add(id, prefix);
                _details.Add(sequence, value);
                Freeze(target.Kind, instance, value);
                prefix.Instance = value.LocalInstanceOrdinal; prefix.Owner = value.CurrentIteratorOwnerOrdinal;
                FlushCore();
                if (!_trace.Healthy) throw new InvalidOperationException("Startup copying lost its bound evidence.");
                return id;
            }
        }

        public void Complete(long id, Il2CppObjectBase instance, bool? originalBool, string path, bool hasPath, bool iteratorPresent)
        {
            lock (_gate)
            {
                if (_stopped) return;
                if (_freezing) { CallbackFailed(); throw new InvalidOperationException("Startup value capture reentered."); }
                if (!_prefixes.TryGetValue(id, out Prefix prefix)) { CallbackFailed(); throw new InvalidOperationException("Startup copied prefix is unavailable."); }
                var value = CopyScalars(prefix.Scalars);
                value.InstanceWrapperPresent = !ReferenceEquals(instance, null);
                value.OriginalBoolReturn = originalBool; value.ReturnedIteratorWrapperPresent = iteratorPresent;
                if (hasPath) value.Paths = new[] { HashPath(prefix.Target.Kind.ToString(), path) };
                Freeze(prefix.Target.Kind, instance, value);
                if (prefix.Instance.HasValue && value.NativeFieldSnapshotCopied)
                    value.PrefixIdentityMatched = prefix.Instance == value.LocalInstanceOrdinal && prefix.Owner == value.CurrentIteratorOwnerOrdinal;
                if (value.PrefixIdentityMatched == false) FailRead(value, "Startup prefix instance or iterator owner changed.");
                long before = _trace.LastSequence;
                bool accepted = _trace.CompleteCall(id, MonotonicNow, false);
                if (_trace.LastSequence != before) _details[_trace.LastSequence] = value;
                FlushCore();
                if (!accepted || !_trace.Healthy) throw new InvalidOperationException("Startup return pairing lost its evidence.");
            }
        }

        public void FinalizeCall(long id, bool originalException)
        {
            lock (_gate)
            {
                if (_stopped) return;
                _prefixes.TryGetValue(id, out Prefix prefix);
                long before = _trace.LastSequence;
                _trace.CompleteCall(id, MonotonicNow, originalException);
                if (_trace.LastSequence != before && prefix != null)
                {
                    var value = CopyScalars(prefix.Scalars);
                    value.ReadError = "Finalizer retained prefix CLR scalars; native fields were not reread.";
                    _details[_trace.LastSequence] = value;
                }
                _prefixes.Remove(id); FlushCore();
            }
        }

        private static SaveStartupObservation CopyScalars(SaveStartupObservation source) => new SaveStartupObservation
        {
            InstanceWrapperPresent = source.InstanceWrapperPresent, DataArgumentWrapperPresent = source.DataArgumentWrapperPresent,
            CompletionDelegateWrapperPresent = source.CompletionDelegateWrapperPresent,
            SaveDataType = source.SaveDataType, SaveSlotType = source.SaveSlotType, SlotIndex = source.SlotIndex
        };
        private static void CopyArguments(SaveStartupMethod kind, object[] args, SaveStartupObservation value)
        {
            if (args == null) return;
            if (kind == SaveStartupMethod.SaveInit || kind == SaveStartupMethod.SaveInitFactory)
                value.CompletionDelegateWrapperPresent = args.Length != 0 && !ReferenceEquals(args[0], null);
            if (kind == SaveStartupMethod.PlayerSetLoaded)
                value.DataArgumentWrapperPresent = args.Length != 0 && !ReferenceEquals(args[0], null);
            if (kind == SaveStartupMethod.FileNameSlot && args.Length == 1 && args[0] is DR.Save.SaveSlotType slot)
                value.SaveSlotType = (int)slot;
            if (args.Length == 3 && args[0] is DR.Save.SaveDataType data && args[1] is int index && args[2] is DR.Save.SaveSlotType fullSlot)
            { value.SaveDataType = (int)data; value.SlotIndex = index; value.SaveSlotType = (int)fullSlot; }
        }

        private void Freeze(SaveStartupMethod kind, Il2CppObjectBase instance, SaveStartupObservation value)
        {
            if (!_trace.CanReadBoundThread())
            {
                if (_trace.FirstUnityUpdateObserved && Environment.CurrentManagedThreadId != _trace.UnityThreadId) _unexpectedThreads++;
                value.ReadError = _trace.FirstUnityUpdateObserved
                    ? "Trace or bound Unity thread is unavailable; no native pointer or field read."
                    : "Before the first actual Unity Update; no native pointer or field read.";
                return;
            }
            _freezing = true;
            try
            {
                value.LocalInstanceOrdinal = Ordinal(instance);
                SaveSystemType system = null;
                if ((int)kind >= (int)SaveStartupMethod.SaveInit && (int)kind <= (int)SaveStartupMethod.FileNameData)
                {
                    if (kind == SaveStartupMethod.SaveInitMove)
                    {
                        var iterator = Read(() => new SaveSystemType._InitSaveSystem_d__42(new IntPtr(Pointer(instance))));
                        value.IteratorState = Read(() => iterator.__1__state);
                        system = Read(() => iterator.__4__this);
                        value.CurrentIteratorOwnerOrdinal = Ordinal(system);
                    }
                    else system = Read(() => new SaveSystemType(new IntPtr(Pointer(instance))));
                }
                if (kind == SaveStartupMethod.GameInitMove)
                {
                    var iterator = Read(() => new GameBase._Init_d__32(new IntPtr(Pointer(instance))));
                    value.IteratorState = Read(() => iterator.__1__state); value.CurrentIteratorOwnerOrdinal = Ordinal(Read(() => iterator.__4__this));
                }
                if (kind == SaveStartupMethod.GameLoadMove)
                {
                    var iterator = Read(() => new GameBase._LoadGameData_d__43(new IntPtr(Pointer(instance))));
                    value.IteratorState = Read(() => iterator.__1__state); value.CurrentIteratorOwnerOrdinal = Ordinal(Read(() => iterator.__4__this));
                }
                if (kind == SaveStartupMethod.GameAfterMove)
                {
                    var iterator = Read(() => new GameBase._InitAfterSaveSystem_d__45(new IntPtr(Pointer(instance))));
                    value.IteratorState = Read(() => iterator.__1__state); value.CurrentIteratorOwnerOrdinal = Ordinal(Read(() => iterator.__4__this));
                }
                if (!ReferenceEquals(system, null))
                {
                    value.IsInitialized = Read(() => system._IsInitialized_k__BackingField);
                    value.IsLoadFinished = Read(() => system._IsLoadFinished_k__BackingField);
                    value.IsGameLoaded = Read(() => system._IsGameLoaded_k__BackingField);
                    value.SkipCloudPullForPreset = Read(() => SaveSystemType.SkipCloudPullForPreset);
                    value.GameManagerPresent = !ReferenceEquals(Read(() => system._GameDataManager), null);
                    value.PlayerManagerPresent = !ReferenceEquals(Read(() => system._PlayerDataManager), null);
                    value.PhotoManagerPresent = !ReferenceEquals(Read(() => system._PhotoDataManager), null);
                    value.UserOptionManagerPresent = !ReferenceEquals(Read(() => system._UserOptionManager), null);
                    var paths = new List<SaveStartupPathEvidence>(value.Paths);
                    paths.Add(HashPath("DefaultSaveFolderField", Read(() => SaveSystemType.DefaultSaveFolder)));
                    paths.Add(HashPath("DefaultSaveFilePathField", Read(() => system._DefaultSaveFilePath_k__BackingField)));
                    value.Paths = paths.ToArray();
                }
                if (kind == SaveStartupMethod.GameCreate || kind == SaveStartupMethod.GameOnLoad)
                { var manager = Read(() => new DR.Save.SaveSystemGameDataManager(new IntPtr(Pointer(instance)))); value.ManagerDataPresent = !ReferenceEquals(Read(() => manager._Data_k__BackingField), null); }
                if (kind == SaveStartupMethod.PlayerCreate || kind == SaveStartupMethod.PlayerOnLoad || kind == SaveStartupMethod.PlayerLoad || kind == SaveStartupMethod.PlayerSetLoaded)
                {
                    var manager = Read(() => new DR.Save.SaveSystemPlayerDataManager(new IntPtr(Pointer(instance))));
                    value.ManagerDataPresent = !ReferenceEquals(Read(() => manager._Data_k__BackingField), null);
                    value.PlayerInteractionPresent = !ReferenceEquals(Read(() => manager._InstanceData_k__BackingField), null);
                }
                if (kind == SaveStartupMethod.PhotoCreate || kind == SaveStartupMethod.PhotoOnLoad)
                { var manager = Read(() => new DR.Save.SaveSystemPhotoDataManager(new IntPtr(Pointer(instance)))); value.ManagerDataPresent = !ReferenceEquals(Read(() => manager._Data_k__BackingField), null); }
                if (kind == SaveStartupMethod.OptionCreate || kind == SaveStartupMethod.OptionOnLoad || kind == SaveStartupMethod.UserOptionAwake)
                { var manager = Read(() => new DR.Save.SaveSystemUserOptionManager(new IntPtr(Pointer(instance)))); value.ManagerDataPresent = !ReferenceEquals(Read(() => manager._Data_k__BackingField), null); }
                GuardNative(); value.NativeFieldSnapshotCopied = true;
            }
            catch { FailRead(value, "Direct startup field snapshot failed; no native values retained."); }
            finally { _freezing = false; }
        }
        private T Read<T>(Func<T> read) { GuardNative(); T value = read(); GuardNative(); return value; }
        private void GuardNative()
        { if (!_trace.CanReadBoundThread()) throw new InvalidOperationException("Startup native thread boundary is unavailable."); }
        private long? Ordinal(Il2CppObjectBase instance)
        {
            if (ReferenceEquals(instance, null)) return null;
            long pointer = Read(() => Pointer(instance));
            if (!_ordinals.TryGetValue(pointer, out long ordinal))
            {
                if (_ordinals.Count >= MaxNativeOrdinals) throw new InvalidOperationException("Startup identity ordinal capacity exceeded.");
                ordinal = _ordinals.Count + 1; _ordinals.Add(pointer, ordinal);
            }
            return ordinal;
        }
        private static long Pointer(Il2CppObjectBase instance)
        { if (ReferenceEquals(instance, null) || instance.Pointer == IntPtr.Zero) throw new InvalidOperationException("Startup instance is missing."); return instance.Pointer.ToInt64(); }
        private void FailRead(SaveStartupObservation value, string reason)
        {
            _readErrors++; _trace.Invalidate(reason);
            value.NativeFieldSnapshotCopied = false; value.LocalInstanceOrdinal = null; value.CurrentIteratorOwnerOrdinal = null;
            value.IteratorState = null; value.IsInitialized = null; value.IsLoadFinished = null; value.IsGameLoaded = null; value.SkipCloudPullForPreset = null;
            value.GameManagerPresent = null; value.PlayerManagerPresent = null; value.PhotoManagerPresent = null; value.UserOptionManagerPresent = null;
            value.ManagerDataPresent = null; value.PlayerInteractionPresent = null; value.PrefixIdentityMatched = null;
            // An original returned CLR string remains scalar evidence; direct
            // native path fields from a partial failed snapshot do not.
            value.Paths = Array.FindAll(value.Paths, p => p.Category != "DefaultSaveFolderField" && p.Category != "DefaultSaveFilePathField");
            value.ReadError = reason;
        }

        private SaveStartupPathEvidence HashPath(string category, string value)
        {
            var result = new SaveStartupPathEvidence { Category = category, Present = value != null, Utf16Length = value?.Length ?? 0 };
            if (value == null) return result;
            if (value.Length > MaxPathCharacters || _pathCharacters > MaxRunPathCharacters - value.Length)
            { result.LimitExceeded = true; _pathLimitFailures++; _trace.Invalidate("Startup path hash budget exceeded."); return result; }
            _pathCharacters += value.Length;
            // Hash exact UTF16 code units (little endian), including unpaired
            // surrogates; no normalization or path/file API is called.
            var bytes = new byte[value.Length * 2];
            for (int i = 0; i < value.Length; i++) { bytes[2 * i] = (byte)value[i]; bytes[2 * i + 1] = (byte)(value[i] >> 8); }
            result.Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); return result;
        }
        private void FlushCore()
        {
            while (_trace.TryTake(out SaveStartupCallObservation observed))
            {
                _details.TryGetValue(observed.Sequence, out SaveStartupObservation value); _details.Remove(observed.Sequence);
                value ??= new SaveStartupObservation(); value.Trace = observed;
                if (_pending.Count >= MaxQueued) { _dropped++; _trace.Invalidate("Startup copied-event queue capacity exceeded."); continue; }
                _pending.Enqueue(value);
            }
        }
        public void CallbackFailed()
        {
            lock (_gate)
            {
                _trace.Invalidate("A startup callback or copied-value boundary failed.");
                // A nested interop callback must not drain the enclosing
                // partially assembled DTO. Its post-read guard loses health.
                if (!_freezing) FlushCore();
            }
        }

        // Only this actual Unity callback supplies the thread marker. Drain
        // serializes owned CLR objects and never dereferences native wrappers.
        public void Update()
        {
            lock (_gate)
            {
                if (!_firstUpdate) { _firstUpdate = true; _trace.ObserveFirstUnityUpdate(MonotonicNow); FlushCore(); }
            }
            if (!_stopped)
            {
                try { _hooks.CheckHealthy(); if (!_trace.Healthy) Stop("Startup evidence failed or its bounded quota was exhausted."); }
                catch { Stop("Startup hook health was lost."); }
            }
            for (int i = 0; i < MaxDrainPerUpdate; i++)
            {
                SaveStartupObservation value;
                lock (_gate) { if (_pending.Count == 0) break; value = _pending.Dequeue(); }
                _logged++; _logger.LogInfo("DAVECOOP_SAVE_STARTUP_CALL: " + JsonSerializer.Serialize(value));
            }
            lock (_gate)
                if (_stopped && _pending.Count == 0) { LogState(true); return; }
            double now = MonotonicNow;
            if (now >= _nextState) { _nextState = now + 5; LogState(); }
        }
        private void LogState(bool terminal = false)
        {
            lock (_gate)
            {
                // Reserve the last of 128 state entries for one terminal
                // summary; periodic logs cannot consume it or reset the cap.
                if (terminal)
                { if (_terminalStateLogged) return; _terminalStateLogged = true; }
                if (_stateLogs >= MaxStateLogs - (terminal ? 0 : 1)) return;
                _stateLogs++;
                _logger.LogInfo("DAVECOOP_SAVE_STARTUP_STATE: " + JsonSerializer.Serialize(new
                {
                    _trace.RunId, _trace.Healthy, _trace.IntegrityLost, _trace.Reason, _trace.PluginLoadObserved, _trace.HooksInstalled,
                    _trace.FirstUnityUpdateObserved, _trace.LateInstallation, _trace.UnityThreadId,
                    _trace.RunEvents, _trace.DroppedEvents, _trace.WrongThreadCalls, _trace.UnmatchedCalls,
                    _trace.OriginalExceptions, _trace.InstallationGaps, _trace.PendingCount, _trace.AbandonedCalls,
                    CopyQueued = _pending.Count, CopyDropped = _dropped, ReadErrors = _readErrors,
                    UnexpectedThreads = _unexpectedThreads, Logged = _logged, PathCharacters = _pathCharacters,
                    PathLimitFailures = _pathLimitFailures,
                    _hooks.TargetCount, _hooks.CallbackErrors, _hooks.CleanupVerified,
                    StateLogs = _stateLogs, StateLogLimit = MaxStateLogs, FinalState = terminal,
                    QueueLimit = MaxQueued, RunEventLimit = SaveStartupTrace.MaxRunEvents, ProcessEventLimit = MaxProcessEvents,
                    PendingLimit = SaveStartupTrace.MaxPendingCalls, NativeOrdinalLimit = MaxNativeOrdinals,
                    PathLimit = MaxPathCharacters, ProcessPathLimit = MaxRunPathCharacters,
                    DrainPerUpdate = MaxDrainPerUpdate, InstallationCoverageVerified = false,
                    StartupCompleteness = false, StartEarlyCoverageVerified = false, FirstLoadOrderVerified = false,
                    NativeHookAbiVerified = false, GuestStateIsolated = false, NativePermission = false, WorldAuthority = false, CargoAuthority = false
                }));
            }
        }
        private void Stop(string reason)
        {
            lock (_gate) { _stopped = true; _trace.Retire(reason); FlushCore(); }
            try { _hooks.Dispose(); }
            catch
            {
                if (!_cleanupWarned) _logger.LogWarning("DAVECOOP_SAVE_STARTUP_CLEANUP_UNVERIFIED: own hook/context references retained; restart required.");
                _cleanupWarned = true;
            }
            Status = _hooks.CleanupVerified ? "Save startup observer: stopped, incomplete evidence" : "Save startup observer: own cleanup unverified";
        }
        public void Dispose() { Stop("Application quit ended startup observation."); LogState(true); }
    }
}
