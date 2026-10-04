using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using DaveCoop.Core.World;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DaveCoop.Networking
{
    // Experimental consumer of the original Init coroutine. This does not
    // authorize world effects, guest persistence or employee cargo operations.
    internal sealed class NativeGuestIgpController
    {
        private const int MaxControllers = MapOriginRegistry.MaxControllers, MaxTraces = 64;
        private const string Owner = Plugin.Id + ".guest-igp-selection";
        private static NativeGuestIgpController _active;
        private readonly NativeGuestMapController _maps;
        private readonly ManualLogSource _logger;
        private MapSelectionCapture _addresses;
        private readonly Dictionary<long, Controller> _controllers = new Dictionary<long, Controller>();
        private readonly Dictionary<long, Controller> _iterators = new Dictionary<long, Controller>();
        private Controller _insideMove;
        private bool _busy;
        private int _traces;
        private string _lastTrace;
        public long AppliedChoices { get; private set; }
        public string Status { get; private set; } = "Awaiting source-bound scene generation.";
        public bool NativeAbiVerified => false;
        public bool WorldAuthority => false;

        private NativeGuestSceneController Sources => _maps.SceneSources;
        private sealed class Controller
        {
            public NativeGuestControllerBirth Birth;
            public Il2CppSystem.Collections.IEnumerator Returned;
            public IGPSetController._Init_d__16 Iterator;
            public long IteratorPointer, SelectedPointer;
            public string Address;
            public MapIgpChoice Choice;
            public NativeGuestIgpSelection Selection;
            public IGPSetInfo Selected;
            public bool FactoryReturned, FirstMove, SelectionStarted, ChoiceUsed, Completed, Retired;
        }
        private sealed class FactoryCall { public Controller State, Previous; public long UnknownScope; public bool Finished; }
        private sealed class MoveCall
        {
            public Controller State, Previous;
            public long Scope;
            public bool UnknownScope, Approved, Finished;
        }

        public NativeGuestIgpController(NativeGuestMapController maps, ManualLogSource logger)
        { _maps = maps ?? throw new ArgumentNullException(nameof(maps)); _logger = logger; }

        public void Install()
        {
            if (_active != null) throw new InvalidOperationException("Another IGP consumer owns this process.");
            _active = this;
            var targets = new List<(MethodInfo Method, string Prefix, string Postfix, string Finalizer)>();
            Add(targets, typeof(IGPSetController), "Init", false, typeof(Il2CppSystem.Collections.IEnumerator), nameof(FactoryBefore), nameof(FactoryAfter), nameof(FactoryFinally));
            Add(targets, typeof(IGPSetController._Init_d__16), "MoveNext", false, typeof(bool), nameof(MoveBefore), nameof(MoveAfter), nameof(MoveFinally));
            Add(targets, typeof(IGPSetController), "GetRandomIGPSetInfo", false, typeof(IGPSetInfo), nameof(ChoiceBefore), null, null);
            Add(targets, typeof(IGPSetController), "IsDoneAllIGPInit", true, typeof(bool), nameof(DoneBefore), null, null);
            Add(targets, typeof(IGPSetController), "OnDestroy", false, typeof(void), nameof(DestroyBefore), null, null);
            var harmony = new Harmony(Owner);
            foreach (var target in targets)
                harmony.Patch(target.Method, prefix: Hook(target.Prefix, Priority.First), postfix: Hook(target.Postfix, Priority.Last), finalizer: Hook(target.Finalizer, Priority.Last));
            if (!targets.All(target => Harmony.GetPatchInfo(target.Method)?.Owners.Contains(Owner) == true))
                throw new InvalidOperationException("Experimental IGP patches are incomplete.");
        }

        private void BeginFactory(IGPSetController native, out FactoryCall call)
        {
            call = new FactoryCall { Previous = _insideMove };
            _insideMove = null;
            if (Sources?.Failed == true) throw Block("The experimental scene source has failed and requires a restart.");
            if (Sources == null || !Sources.VerifyGenerationWindow()) return;
            Enter();
            try
            {
                Sources.EnterUnknownScope(out long unknownScope); call.UnknownScope = unknownScope;
                NativeGuestControllerBirth birth = Sources.RegisterControllerBirth(native);
                if (birth == null) return;
                if (_controllers.Count >= MaxControllers || _controllers.ContainsKey(birth.ControllerPointer))
                    throw Block("A scene generation controller has repeated or exhausted its fixed lifetime.");
                var state = new Controller { Birth = birth };
                _controllers.Add(birth.ControllerPointer, state);
                call.State = state;
            }
            finally { _busy = false; }
        }

        private void EndFactory(FactoryCall call, Il2CppSystem.Collections.IEnumerator returned, bool ranOriginal)
        {
            if (call?.State == null || call.Finished) return;
            call.Finished = true;
            Enter();
            try
            {
                Controller state = call.State;
                if (!ranOriginal || state.FactoryReturned || ReferenceEquals(returned, null) || !Fresh(state))
                    throw Block("The original IGP factory did not return its paired live iterator.");
                long pointer = Pointer(returned);
                IntPtr expected = Read(state, () => Il2CppClassPointerStore<IGPSetController._Init_d__16>.NativeClassPtr);
                if (pointer == 0 || expected == IntPtr.Zero || Read(state, () => IL2CPP.il2cpp_object_get_class(returned.Pointer)) != expected)
                    throw Block("The original IGP factory returned an unsupported iterator.");
                var iterator = Read(state, () => new IGPSetController._Init_d__16(returned.Pointer));
                if (Read(state, () => Pointer(iterator.__4__this)) != state.Birth.ControllerPointer ||
                    Read(state, () => iterator.__1__state) != 0 || Read(state, () => iterator.__2__current) != null)
                    throw Block("The fixed IGP factory result advanced or changed its actor before binding.");
                Sources.RegisterControllerIterator(state.Birth, returned);
                state.Returned = returned; state.Iterator = iterator; state.IteratorPointer = pointer;
                if (_iterators.ContainsKey(pointer)) throw Block("An IGP iterator belongs to two controller lifetimes.");
                _iterators.Add(pointer, state); state.FactoryReturned = true;
                if (!Fresh(state)) throw Block("The IGP birth source expired during factory binding.");
            }
            finally { _busy = false; }
        }

        private bool BeginMove(IGPSetController._Init_d__16 iterator, ref bool result, out MoveCall call)
        {
            call = new MoveCall { Previous = _insideMove };
            _insideMove = null; // An unknown nested coroutine cannot borrow its parent.
            if (Sources?.Failed == true) throw Block("The experimental scene source has failed and cannot resume initialization.");
            if (Sources == null || Sources.UnityThreadId == 0) return true;
            bool currentWindow = Sources.VerifyGenerationWindow();
            Enter();
            try
            {
                long pointer = Pointer(iterator);
                if (!_iterators.TryGetValue(pointer, out Controller state))
                {
                    if (currentWindow)
                    { Sources.EnterUnknownScope(out long scope); call.Scope = scope; call.UnknownScope = true; }
                    return true;
                }
                call.State = state;
                if (!state.FactoryReturned || !Fresh(state))
                    throw Block("The original IGP iterator resumed outside its fixed live source.");
                IntPtr expected = Read(state, () => Il2CppClassPointerStore<IGPSetController._Init_d__16>.NativeClassPtr);
                if (Pointer(iterator) != state.IteratorPointer || Pointer(state.Returned) != pointer || expected == IntPtr.Zero ||
                    Read(state, () => IL2CPP.il2cpp_object_get_class(iterator.Pointer)) != expected ||
                    Read(state, () => Pointer(iterator.__4__this)) != state.Birth.ControllerPointer)
                    throw Block("The original IGP iterator or actor changed.");
                int nativeState = iterator.__1__state;
                if (!state.FirstMove)
                {
                    if (nativeState != 0 || iterator.__2__current != null) throw Block("The original IGP iterator advanced before its first approved move.");
                    state.FirstMove = true;
                }
                if (!state.ChoiceUsed)
                {
                    // These are the original dependency-wait states, before
                    // controller registration/random selection. Do not mutate
                    // their state/current or add a fabricated registration.
                    if (nativeState < 0 || nativeState > 2 || iterator.__2__current != null ||
                        state.Birth.Controller._IsInitDone_k__BackingField || state.Birth.Controller.CurrIGPSetInfo != null ||
                        state.Birth.Controller.CurrIGPSet != null || !Fresh(state))
                        throw Block("The IGP preselection iterator or native controller already changed.");
                    if (!Sources.TryReadControllerSource(state.Birth, out MapOriginControllerSource source))
                    { result = true; Trace("WAITING_SOURCE", "Holding original scene generation for its exact preexisting scene operation."); return false; }
                    if (state.Address == null)
                    {
                        if (_addresses == null) _addresses = new MapSelectionCapture(Sources.UnityThreadId);
                        state.Address = _addresses.ReadControllerAddress(state.Birth.Controller);
                        if (string.IsNullOrEmpty(state.Address) || !Fresh(state)) throw Block("The source-bound IGP hierarchy address changed.");
                    }
                    if (!Sources.TryCaptureChoice(state.Birth, state.Address, out MapIgpChoice choice))
                    { result = true; Trace("WAITING_CHOICE", "Holding original scene generation for the current host choice."); return false; }
                    if (!AddressCurrent(state)) throw Block("The current IGP hierarchy no longer matches its host choice address.");
                    if (state.Choice == null)
                    {
                        if (state.SelectionStarted) throw Block("An unknown IGP selection attempt cannot be retried.");
                        state.SelectionStarted = true; state.Choice = MapChoiceFrames.Copy(choice);
                        state.Selection = new NativeGuestIgpSelection(state.Birth.Controller, state.Choice, () => FreshChoice(state));
                        if (!state.Selection.TryResolve(out IGPSetInfo selected) || ReferenceEquals(selected, null))
                            throw Block("The host IGP choice has no unique current local resource.");
                        state.Selected = selected; state.SelectedPointer = Pointer(selected);
                    }
                    if (!FreshChoice(state) || !state.Selection.Validate()) throw Block("The fixed local IGP choice expired before its original move.");
                }
                else if (!FreshChoice(state) || !AddressCurrent(state) || !state.Selection.ValidateAfterOriginalChoice())
                    throw Block("A previously adopted IGP choice changed during original initialization.");
                if (call.Previous != null) throw Block("Two source-bound IGP moves nested inside one initialization.");
                if (!Sources.EnterControllerMove(state.Birth, iterator, out long boundScope))
                    throw Block("The selected IGP lost its exact scene source before original initialization.");
                call.Scope = boundScope; call.Approved = true; _insideMove = state;
                return true;
            }
            finally { _busy = false; }
        }

        private bool Select(IGPSetController native, ref IGPSetInfo result)
        {
            if (Sources?.Failed == true) throw Block("A failed experimental scene cannot select another local random IGP.");
            if (Sources != null) Sources.VerifyGenerationWindow();
            Controller state = _insideMove;
            if (state == null) return true;
            Enter();
            try
            {
                if (Pointer(native) != state.Birth.ControllerPointer) return true;
                if (state.ChoiceUsed || !FreshChoice(state) || !AddressCurrent(state) || state.Selection == null || !state.Selection.Validate() || Pointer(state.Selected) != state.SelectedPointer)
                    throw Block("The original IGP selection repeated or lost its adopted resource.");
                state.ChoiceUsed = true; // Single attempt, before publishing the result.
                result = state.Selected;
                if (!FreshChoice(state)) throw Block("The source changed while supplying its selected IGP info.");
                AppliedChoices++;
                Trace("CHOICE_SUPPLIED", "Supplying one source-bound host choice to the original local scene generator; native execution remains unverified.");
                return false;
            }
            finally { _busy = false; }
        }

        private void EndMove(MoveCall call, bool ranOriginal, bool result)
        {
            if (call == null || call.Finished) return;
            call.Finished = true;
            if (!call.Approved) return;
            Enter();
            try
            {
                Controller state = call.State;
                if (!ranOriginal || !FreshChoice(state) ||
                    !(state.ChoiceUsed ? state.Selection.ValidateAfterOriginalChoice() : state.Selection.Validate()))
                    throw Block("The approved original IGP move was skipped or its source expired.");
                if (state.ChoiceUsed && Pointer(state.Birth.Controller.CurrIGPSetInfo) != state.SelectedPointer)
                    throw Block("Original initialization did not keep the supplied IGP info.");
                if (!result)
                {
                    if (!state.ChoiceUsed || !state.Birth.Controller._IsInitDone_k__BackingField || state.Birth.Controller.CurrIGPSet == null || !FreshChoice(state))
                        throw Block("The original IGP coroutine ended without its selected initialized set.");
                    state.Completed = true;
                }
            }
            finally { _busy = false; }
        }

        private void FinishMove(MoveCall call, Exception error)
        {
            if (call == null) return;
            try
            {
                if (error != null && call.State != null)
                { call.State.Retired = true; Sources.SourceFailure("Original IGP callback exception: " + error.GetType().Name); }
                if (call.Scope != 0)
                {
                    if (call.UnknownScope) Sources.ExitUnknownScope(call.Scope);
                    else Sources.ExitControllerMove(call.State.Birth, call.Scope);
                }
            }
            finally { _insideMove = call.Previous; }
        }

        private bool Fresh(Controller state) => state != null && !state.Retired && Sources != null &&
            Sources.ValidateControllerBirth(state.Birth) && !state.Retired;
        private T Read<T>(Controller state, Func<T> read)
        {
            if (!Fresh(state)) throw Block("The fixed IGP source expired before a native read.");
            T result = read();
            if (!Fresh(state)) throw Block("The fixed IGP source changed during a native read.");
            return result;
        }
        private bool FreshChoice(Controller state) => Fresh(state) && state.Choice != null &&
            Sources.TryCaptureChoice(state.Birth, state.Address, out MapIgpChoice current) && SameResource(state.Choice, current) && Fresh(state);
        private bool AddressCurrent(Controller state) => Fresh(state) && _addresses != null &&
            _addresses.ReadControllerAddress(state.Birth.Controller) == state.Address && Fresh(state);
        private static bool SameResource(MapIgpChoice fixedChoice, MapIgpChoice current) => current != null &&
            fixedChoice.Generation == current.Generation && fixedChoice.RouteFingerprint == current.RouteFingerprint &&
            current.Revision >= fixedChoice.Revision && fixedChoice.SceneId == current.SceneId &&
            fixedChoice.ControllerAddress == current.ControllerAddress && fixedChoice.Addressable == current.Addressable &&
            (fixedChoice.SelectedPrefabName ?? "") == (current.SelectedPrefabName ?? "") &&
            (fixedChoice.Addressable || (fixedChoice.PrefabObjectName ?? "") == (current.PrefabObjectName ?? ""));

        private bool AllowDone(ref bool result)
        {
            if (Sources == null || Sources.Failed) { result = false; return false; }
            if (_controllers.Count == 0)
            {
                if (Sources.RequiresIgpWait()) { result = false; return false; }
                return true;
            }
            if (Sources.UnityThreadId == 0 || Environment.CurrentManagedThreadId != Sources.UnityThreadId)
                throw Block("The global IGP completion callback changed its confirmed Unity thread.");
            Controller[] current = _controllers.Values.Where(state => Sources.IsCurrentEntry(state.Birth)).ToArray();
            if (current.Length == 0)
            {
                if (Sources.RequiresIgpWait()) { result = false; return false; }
                return true;
            }
            // Waiting controllers have not entered the original registration
            // list. Its empty/all-done result cannot complete this expedition.
            if (current.Any(state => state.Retired || !state.Completed))
            { result = false; return false; }
            if (Sources == null || !Sources.Available) { result = false; return false; }
            Enter();
            try
            {
                foreach (Controller state in current)
                    if (!FreshChoice(state) || !state.Birth.Controller._IsInitDone_k__BackingField ||
                        Pointer(state.Birth.Controller.CurrIGPSetInfo) != state.SelectedPointer || !state.Selection.ValidateAfterOriginalChoice())
                    { result = false; return false; }
                return true;
            }
            finally { _busy = false; }
        }

        private void Retire(IGPSetController native)
        {
            // Destruction may already have cleared the Unity pointer. Use the
            // retained wrapper identity only; never read scene/fields here.
            if (ReferenceEquals(native, null)) return;
            if (_controllers.TryGetValue(Pointer(native), out Controller state))
            { state.Retired = true; Sources.RetireController(state.Birth); }
        }
        private void Enter()
        {
            if (_busy || Sources == null || Sources.Failed || Sources.UnityThreadId == 0 || Environment.CurrentManagedThreadId != Sources.UnityThreadId)
                throw Block("IGP callback reentered or changed its confirmed Unity thread.");
            _busy = true;
        }
        private Exception Block(string reason)
        { Status = reason; Sources?.SourceFailure("Experimental IGP initialization blocked: " + reason); return new InvalidOperationException("MultiDave temporary scene generation was blocked."); }
        private void Trace(string stage, string text)
        {
            Status = text;
            string value = stage + ": " + text;
            if (_traces >= MaxTraces || _lastTrace == value) return;
            _lastTrace = value; _traces++;
            try { _logger.LogInfo("DAVECOOP_GUEST_IGP_" + stage + ": " + text); }
            catch { Sources.SourceFailure("Experimental IGP diagnostic callback failed."); }
        }
        private static long Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? 0 : value.Pointer.ToInt64();
        private static HarmonyMethod Hook(string name, int priority) => name == null ? null :
            new HarmonyMethod(typeof(NativeGuestIgpController).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)) { priority = priority };
        private static void Add(List<(MethodInfo, string, string, string)> targets, Type type, string name, bool isStatic, Type result, string prefix, string postfix, string finalizer)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, Type.EmptyTypes, null);
            if (method == null || method.DeclaringType != type || method.IsStatic != isStatic || method.IsGenericMethod || method.ReturnType != result)
                throw new InvalidOperationException("Unsupported IGP declaration: " + type.Name + "." + name);
            targets.Add((method, prefix, postfix, finalizer));
        }

        private static void FactoryBefore(IGPSetController __instance, out FactoryCall __state)
        {
            __state = null;
            try { if (_active != null) _active.BeginFactory(__instance, out __state); }
            catch (Exception error) { _active?.Sources?.SourceFailure("IGP factory prefix failed: " + error.GetType().Name); throw; }
        }
        private static void FactoryAfter(Il2CppSystem.Collections.IEnumerator __result, bool __runOriginal, FactoryCall __state) => _active?.EndFactory(__state, __result, __runOriginal);
        private static Exception FactoryFinally(Exception __exception, FactoryCall __state)
        {
            if (_active != null && __state != null)
            {
                try
                {
                    if (__exception != null && __state.State != null)
                    { __state.State.Retired = true; _active.Sources.SourceFailure("Original IGP factory exception: " + __exception.GetType().Name); }
                    if (__state.UnknownScope != 0) _active.Sources.ExitUnknownScope(__state.UnknownScope);
                }
                finally { _active._insideMove = __state.Previous; }
            }
            return __exception;
        }
        private static bool MoveBefore(IGPSetController._Init_d__16 __instance, ref bool __result, out MoveCall __state)
        {
            __state = null;
            try { return _active == null || _active.BeginMove(__instance, ref __result, out __state); }
            catch (Exception error) { _active?.Sources?.SourceFailure("IGP move prefix failed: " + error.GetType().Name); throw; }
        }
        private static void MoveAfter(bool __runOriginal, bool __result, MoveCall __state) => _active?.EndMove(__state, __runOriginal, __result);
        private static Exception MoveFinally(Exception __exception, MoveCall __state)
        { _active?.FinishMove(__state, __exception); return __exception; }
        private static bool ChoiceBefore(IGPSetController __instance, ref IGPSetInfo __result)
        {
            try { return _active == null || _active.Select(__instance, ref __result); }
            catch (Exception error) { _active?.Sources?.SourceFailure("IGP selection prefix failed: " + error.GetType().Name); throw; }
        }
        private static bool DoneBefore(ref bool __result)
        {
            try { return _active == null || _active.AllowDone(ref __result); }
            catch (Exception error) { _active?.Sources?.SourceFailure("IGP completion prefix failed: " + error.GetType().Name); throw; }
        }
        private static void DestroyBefore(IGPSetController __instance) => _active?.Retire(__instance);
    }
}
