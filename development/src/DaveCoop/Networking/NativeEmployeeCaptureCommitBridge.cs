using System;
using System.Collections.Generic;
using System.Reflection;
using DaveCoop.Core.Cargo;
using DaveCoop.Core.World;
using DR.AI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace DaveCoop.Networking
{
    // Experimental whole-corpse Harvest policy. Its receipt covers the fixed
    // employee batch and the explicitly dispatched looting/codex/mission steps;
    // it does not claim equivalence with all original unlock/HUD/achievement
    // callbacks. Native ABI and runtime acceptance still require game testing.
    internal sealed class NativeEmployeeCaptureCommitBridge
    {
        public const int MaxOwners = 256, MaxReferences = 32, MaxSteps = 262144, MaxProgressEntries = 4096;
        public const float InteractionDistance = 2f;
        private const string HookOwner = Plugin.Id + ".employee-harvest";
        private static readonly object OwnershipSync = new object();
        private static readonly List<NativeEmployeeCaptureCommitBridge> Owners = new List<NativeEmployeeCaptureCommitBridge>();
        private static readonly Dictionary<long, NativeEmployeeCaptureCommitBridge> FishOwners = new Dictionary<long, NativeEmployeeCaptureCommitBridge>();
        private static Harmony _harmony;
        private static List<MethodInfo> _hookMethods;
        private static bool _hookFailure;
        private static volatile NativeEmployeeCaptureCommitBridge _executing;
        private readonly NativeEmployeeCaptureCommand _command;
        private readonly NativeHostCargoSource _host;
        private readonly FishStateCapture _capture;
        private readonly FishLifecycleHooks _lifecycle;
        private readonly int _thread;
        private readonly List<Reference> _references = new List<Reference>(MaxReferences);
        private NativeHostCargoSnapshot _before;
        private SaveData _save;
        private LootBox _bag;
        private MissionManager _mission;
        private FishAISystem _fish;
        private SABaseFishSystem _special;
        private DR.FishInfoData _info;
        private FishInteractionBody _body;
        private Damageable _damageable;
        private GameObject _root;
        private UniRx.ReactiveProperty<bool> _dead, _corpse, _captured, _hooked;
        private HostEntityTarget _target;
        private CargoSourceIntent _intent;
        private CargoSourceLease _lease;
        private NativeEmployeeFishSelectionBridge _selection;
        private CargoProduct[] _products;
        private DR.IItemBase[] _items;
        private IntPtr _fishPointer, _fishUnity, _fishClass, _bodyPointer, _rootPointer, _rootUnity;
        private IntPtr _infoPointer, _savePointer, _bagPointer, _missionPointer;
        private int _steps, _codexGrade;
        private bool _busy, _checking, _prepared, _attempted, _terminalPermit, _terminalReturned;
        private volatile bool _failed, _stopped, _fenced, _bagWriteAttempted;
        private bool _terminalEntered, _receipt;
        public string Status { get; private set; } = "Empty";
        public bool Failed => _failed;
        public bool Entered => _lease != null && _command.Binding.Ledger.IsSelectionEntered(_lease);
        public bool CaptureConfirmed => _receipt;
        public bool HarvestReceiptVerified => _receipt;
        public bool FullOriginalProgressEquivalent => false;
        public bool NativeAbiVerified => false;
        public bool NativeRuntimeVerified => false;
        public bool CrashSafeExactlyOnce => false;
        public long CaptureId => _lease?.CaptureId ?? 0;
        public int ProgressCallsEntered { get; private set; }
        public int ProgressCallsReturned { get; private set; }
        public bool TerminalReadbackObserved { get; private set; }
        public bool NoHostBagWriteObserved { get; private set; }
        public NativeEmployeeFishSelectionBridge Selection => _selection;

        private sealed class Reference
        {
            public Il2CppObjectBase Wrapper;
            public IntPtr Pointer, Handle;
        }
        private sealed class Rejected : Exception
        {
            public readonly string Reason;
            public Rejected(string reason) { Reason = reason; }
        }

        private NativeEmployeeCaptureCommitBridge(NativeEmployeeCaptureCommand command, NativeHostCargoSource host,
            FishStateCapture capture, FishLifecycleHooks lifecycle, int thread)
        { _command = command; _host = host; _capture = capture; _lifecycle = lifecycle; _thread = thread; }

        // Constructor inputs are actual opaque producers. No target, products,
        // capability flags or native resource can be supplied by a client.
        public static bool TryPrepare(NativeEmployeeCaptureCommand command, NativeHostCargoSource host,
            FishStateCapture capture, FishLifecycleHooks lifecycle, int confirmedThread,
            out NativeEmployeeCaptureCommitBridge bridge)
        {
            bridge = null;
            if (command == null || !command.AdmissionEntered || host == null || capture == null || lifecycle == null || confirmedThread < 1 ||
                Environment.CurrentManagedThreadId != confirmedThread || _hookFailure || _executing != null || Owners.Count >= MaxOwners) return false;
            bridge = new NativeEmployeeCaptureCommitBridge(command, host, capture, lifecycle, confirmedThread);
            NativeEmployeeCaptureCommitBridge owner = bridge;
            // A failed handle/patch acquisition must not lose its managed owner.
            lock (OwnershipSync) Owners.Add(bridge);
            bridge._busy = true;
            try
            {
                bridge.Check(false);
                if (!host.TryReadActiveDive(out bridge._before) || !host.IsCurrent(bridge._before) ||
                    !host.TryReadCommitRoots(bridge._before, out bridge._save, out bridge._bag)) throw new Rejected("HostDiveUnavailable");
                if (bridge._before.RootIdentity != command.HostDiveRootIdentity || bridge._before.Generation != command.HostDiveGeneration)
                    throw new Rejected("ExpeditionMismatch");
                bridge._savePointer = bridge.Read(() => Pointer(owner._save));
                bridge._bagPointer = bridge.Read(() => Pointer(owner._bag));
                if (bridge._savePointer == IntPtr.Zero || bridge._bagPointer == IntPtr.Zero) throw new Rejected("HostRootsUnavailable");
                bridge.RequireClass(bridge._save, typeof(SaveData)); bridge.RequireClass(bridge._bag, typeof(LootBox));
                bridge._mission = bridge.Read(() => Singleton<MissionManager>._instance);
                bridge._missionPointer = bridge.Read(() => Pointer(owner._mission));
                if (bridge._missionPointer == IntPtr.Zero) throw new Rejected("MissionSourceUnavailable");
                bridge.RequireClass(bridge._mission, typeof(MissionManager));
                bridge.FindNearestSupportedFish();
                bridge.Keep(bridge._save); bridge.Keep(bridge._bag); bridge.Keep(bridge._mission);
                bridge.Keep(bridge._fish); bridge.Keep(bridge._info); bridge.Keep(bridge._body); bridge.Keep(bridge._damageable);
                bridge.Keep(bridge._root);
                if (!ReferenceEquals(bridge._dead, null)) bridge.Keep(bridge._dead);
                bridge.Keep(bridge._corpse); bridge.Keep(bridge._captured); bridge.Keep(bridge._hooked);
                // Prove bounded, readable original progress containers before
                // any native selector, pity mutation or terminal is entered.
                bridge.ReadLooting(0, requireKey: false);
                bridge.ReadCaught(bridge._target.DataTid, requireKey: false);
                bridge.RequireProgressBudget();
                bridge.ValidateFish(); bridge.RequireUnchangedBag();
                EnsureHooks();
                bridge.Check(false); bridge.ValidateFish();
                if (FishOwners.TryGetValue(bridge._fishPointer.ToInt64(), out NativeEmployeeCaptureCommitBridge previous) &&
                    !previous.IsProvenLaterBirth(bridge._fish)) throw new Rejected("FishAlreadyReserved");
                FishOwners[bridge._fishPointer.ToInt64()] = bridge; bridge._fenced = true;
                bridge._prepared = true; bridge.Status = "SupportedDownedPickupPrepared"; return true;
            }
            catch (Rejected error) { bridge.Fail(error.Reason); return false; }
            catch (Exception error) { bridge.Fail("PrepareUnknown:" + error.GetType().Name); return false; }
            finally { bridge._busy = false; }
        }

        public bool TryCaptureOnce()
        {
            if (Environment.CurrentManagedThreadId != _thread || !_prepared || _attempted || _failed || _stopped || _busy || _executing != null) return false;
            _attempted = true; _busy = true; _steps = 0; _executing = this;
            try
            {
                Check(true); ValidateFish(); RequireUnchangedBag();
                CargoMemberSnapshot member = Employee();
                _intent = new CargoSourceIntent
                {
                    ExpeditionId = _command.Binding.ExpeditionId, MemberId = _command.MemberId,
                    RequestId = _command.InputSequence, BagRevision = member.BagRevision,
                    ActorRevision = _command.ActorRevision, LoadoutRevision = _command.LoadoutRevision,
                    Source = new CargoSource { RoomId = _command.RoomId, SceneEpoch = _target.SceneEpoch,
                        EntityId = _target.EntityId, LocalGeneration = _target.Generation }
                };
                CargoResult reserved = _command.Binding.Ledger.SourceReserve(_intent, SourceFacts(), _command.Now, out _lease);
                if (!reserved.Accepted) throw new Rejected("Reservation:" + reserved.Reason);
                Check(true); RequireUnchangedBag();
                if (!NativeEmployeeFishSelectionBridge.TryPrepare(_command.Binding.Ledger, _lease, _capture, _lifecycle,
                    _thread, FishYieldRecipeKind.OrdinaryPickup, out _selection)) throw new Rejected("SelectionPreparationUnavailable");
                if (!_selection.SelectOnce(SourceFacts(), _command.Now)) throw new Rejected("SelectionUnknown");
                RequireUnchangedBag(); ValidateFish();
                if (!_selection.NormalizeCaptureProductsOnce()) throw new Rejected("NormalizationUnknown");
                RequireUnchangedBag();
                FishProductNormalizationSnapshot products = _selection.Products;
                if (products == null || products.Stage != FishProductNormalizationStage.CaptureProductsHeld || products.Products.Length < 1)
                    throw new Rejected("ProductsIncomplete");
                var modes = new FishReturnCountMode[products.Products.Length];
                // The observed automatic original return path supplies every
                // slot's GetExchangeCount delegate. Direct UI return is a
                // different policy and is not guessed from an ItemType value.
                for (int i = 0; i < modes.Length; i++) modes[i] = FishReturnCountMode.ExchangeWholeOnce;
                if (!_selection.MapReturnProductsOnce(modes)) throw new Rejected("ReturnMappingUnknown");
                RequireUnchangedBag();
                if (!_selection.TryReadHarvestSelection(_command.Binding.Ledger, _lease, out FishAISystem selectedFish,
                    out DR.FishInfoData selectedInfo, out FishInteractionBody selectedBody, out _products, out _items, out int bonus) ||
                    Pointer(selectedFish) != _fishPointer || Pointer(selectedInfo) != _infoPointer || Pointer(selectedBody) != _bodyPointer)
                    throw new Rejected("FrozenSelectionMismatch");
                _codexGrade = checked(bonus + 1);
                if (_codexGrade < 1 || _codexGrade > 1000) throw new Rejected("CodexGradeUnsupported");
                CargoLateYieldFacts late = LateFacts();
                CargoResult sealedResult = _selection.TrySealCaptureProducts(late, _command.Now);
                if (!sealedResult.Accepted) throw new Rejected("PersonalCapacity:" + sealedResult.Reason);
                // All products/count mappings are held and personal capacity
                // accepted before dispatching any shared-progress/terminal.
                ValidateFish(); RequireUnchangedBag();
                int? oldGrade = ReadCaught(_target.DataTid, requireKey: false);
                for (int i = 0; i < _products.Length; i++)
                {
                    int index = i;
                    Progress(() => _save.AddLootingSaveData(_products[index].ProductId, true));
                    if (!ReadLooting(_products[i].ProductId, requireKey: true)) throw new Rejected("LootingReadbackUnavailable");
                    Progress(() => _mission.UpdateMissionIntCondition(_items[index], _products[index].Grade, _products[index].Count));
                }
                Progress(() => _save.AddCaughtFish(_info, _codexGrade, false));
                int? actualGrade = ReadCaught(_target.DataTid, requireKey: true);
                if (!actualGrade.HasValue || actualGrade.Value != Math.Max(oldGrade ?? _codexGrade, _codexGrade))
                    throw new Rejected("CodexReadbackMismatch");
                ValidateFish(); RequireUnchangedBag();
                _terminalEntered = true; _terminalPermit = true; Status = "TerminalEnteredUnknown";
                try { _fish.DestroySelf(); }
                finally { _terminalPermit = false; }
                // Store original paired-return evidence before any following
                // native read. The postfix records __runOriginal independently.
                Check(false); RequireUnchangedBag(); ValidateTerminal();
                if (!_terminalReturned || _bagWriteAttempted || ProgressCallsReturned != _products.Length * 2 + 1)
                    throw new Rejected("HarvestCompletionUnavailable");
                CargoCaptureFacts receipt = ReceiptFacts();
                CargoResult confirmed = _command.Binding.Ledger.ConfirmCapture(_lease.CaptureId,
                    CargoReceiptKind.EmployeeDivertedYield, _products, receipt, _command.Now);
                if (!confirmed.Accepted) throw new Rejected("Confirmation:" + confirmed.Reason);
                _receipt = true; NoHostBagWriteObserved = true;
                // Keep a native-source tombstone after confirmation. Original
                // UnityEvents/late pickup callbacks for the retired fish must
                // not add the same yield to the host's bag. A later, actually
                // observed higher-generation pool birth is treated separately.
                Status = "EmployeeHarvestConfirmed"; return true;
            }
            catch (Rejected error) { Fail(error.Reason); return false; }
            catch (Exception error) { Fail("HarvestUnknown:" + error.GetType().Name); return false; }
            finally { _terminalPermit = false; _busy = false; if (ReferenceEquals(_executing, this)) _executing = null; }
        }

        public void Stop(string reason = "Stopped")
        {
            _stopped = true; if (!_receipt) Status = reason ?? "Stopped";
            // Partial progress/selection/terminal is never replayed. Unknown
            // owners, strong handles and exact source fence remain retained.
        }

        private void FindNearestSupportedFish()
        {
            if (!_command.TryReadPosition(out NVector3 position) || !Finite(position)) throw new Rejected("ActorPositionUnavailable");
            var found = Read(() => UnityEngine.Object.FindObjectsOfType<FishAISystem>());
            int length = Read(() => found.Length);
            if (length > WorldFrames.MaxEntities) throw new Rejected("FishRosterQuota");
            double nearest = InteractionDistance * InteractionDistance; bool tie = false;
            for (int i = 0; i < length; i++)
            {
                int index = i; FishAISystem fish = Read(() => found[index]);
                if (ReferenceEquals(fish, null)) continue;
                IntPtr pointer = Read(() => Pointer(fish)); if (pointer == IntPtr.Zero) continue;
                HostEntityTarget? target = _capture.ResolveObservedPointer(pointer.ToInt64());
                if (!target.HasValue || target.Value.SceneEpoch != _command.SceneEpoch || target.Value.Kind != EntityKind.Fish ||
                    (FishOwners.TryGetValue(pointer.ToInt64(), out NativeEmployeeCaptureCommitBridge previous) && !previous.IsProvenLaterBirth(fish)) ||
                    !_capture.TryResolveNativeFish(target.Value.SceneEpoch, target.Value.EntityId, out FishAISystem current) ||
                    Read(() => Pointer(current)) != pointer) continue;
                IntPtr nativeClass = Read(() => IL2CPP.il2cpp_object_get_class(pointer));
                if (nativeClass != Read(() => Il2CppClassPointerStore<FishAISystem>.NativeClassPtr) &&
                    nativeClass != Read(() => Il2CppClassPointerStore<SABaseFishSystem>.NativeClassPtr)) continue;
                GameObject root = Read(() => fish.gameObject);
                if (Read(() => root.scene.handle) != _before.SceneHandle || !Read(() => root.activeInHierarchy)) continue;
                FishInteractionBody body = Read(() => fish._fishInteractionBody);
                DR.FishInfoData info = Read(() => fish._FishInfoData);
                Damageable damageable = Read(() => fish._fishDamageable);
                var corpse = Read(() => fish.IsCorpseRP); var captured = Read(() => fish._isFishCapturedRP); var hooked = Read(() => fish.IsHookedRP);
                if (ReferenceEquals(body, null) || ReferenceEquals(info, null) || ReferenceEquals(damageable, null) ||
                    ReferenceEquals(corpse, null) || ReferenceEquals(captured, null) || ReferenceEquals(hooked, null)) continue;
                // Only the exact SABase native class declares this direct RP.
                // The base FishAISystem profile instead uses its current exact
                // Damageable.m_IsDead and never invokes a guessed death getter.
                SABaseFishSystem special = nativeClass == Read(() => Il2CppClassPointerStore<SABaseFishSystem>.NativeClassPtr)
                    ? Read(() => body._ownerFish) : null;
                if (nativeClass == Read(() => Il2CppClassPointerStore<SABaseFishSystem>.NativeClassPtr) &&
                    (ReferenceEquals(special, null) || Read(() => Pointer(special)) != pointer)) continue;
                UniRx.ReactiveProperty<bool> dead = ReferenceEquals(special, null) ? null : Read(() => special._IsDeadRP_k__BackingField);
                if (!ReferenceEquals(special, null) && ReferenceEquals(dead, null)) continue;
                if (Read(() => body.InteractionType) != FishInteractionBody.FishInteractionType.Pickup || Read(() => Pointer(body._ownerFish)) != pointer ||
                    (!ReferenceEquals(dead, null) && !Read(() => dead.value)) || !Read(() => damageable.m_IsDead) || Read(() => corpse.value) || Read(() => captured.value) || Read(() => hooked.value) ||
                    Read(() => fish._IsFishHookedSequence_k__BackingField) || !ReferenceEquals(Read(() => fish._FishJoint_k__BackingField), null) ||
                    !ReferenceEquals(Read(() => fish._m_fishmonBody_k__BackingField), null) || Read(() => fish._IsFishSleeped_k__BackingField)) continue;
                if (Read(() => info._TID_k__BackingField) != target.Value.DataTid || Read(() => info._DLCType_k__BackingField) != 0 ||
                    Read(() => info._FishCollectionNotAvailable_k__BackingField) || !Read(() => info._IsIncludeCollectionProgress_k__BackingField)) continue;
                int collectionTid = Read(() => info._FishCollectionFishTID_k__BackingField), tiers = Read(() => info._CarvableCount_k__BackingField);
                if ((collectionTid > 0 && collectionTid != target.Value.DataTid) || Math.Max(1, tiers) > FishYieldRecipe.MaxMainTiers) continue;
                Transform transform = Read(() => fish.transform); Vector3 p = Read(() => transform.position);
                if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) || Math.Abs((double)p.z - position.Z) > 0.01) continue;
                double dx = (double)p.x - position.X, dy = (double)p.y - position.Y, distance = dx * dx + dy * dy;
                if (distance > nearest) continue;
                if (!ReferenceEquals(_fish, null) && distance == nearest) { tie = true; continue; }
                nearest = distance; tie = false; _fish = fish; _special = special; _info = info; _body = body; _damageable = damageable;
                _root = root; _dead = dead; _corpse = corpse; _captured = captured; _hooked = hooked; _target = target.Value;
                _fishPointer = pointer; _fishClass = nativeClass;
            }
            if (ReferenceEquals(_fish, null) || tie) throw new Rejected(tie ? "AmbiguousNearestFish" : "NoSupportedDownedPickup");
            _fishUnity = Read(() => _fish.m_CachedPtr); _rootPointer = Read(() => Pointer(_root)); _rootUnity = Read(() => _root.m_CachedPtr);
            _infoPointer = Read(() => Pointer(_info)); _bodyPointer = Read(() => Pointer(_body));
            if (_fishUnity == IntPtr.Zero || _rootUnity == IntPtr.Zero) throw new Rejected("FishNativeIdentityUnavailable");
            RequireClass(_info, typeof(DR.FishInfoData)); RequireClass(_body, typeof(FishInteractionBody)); RequireClass(_damageable, typeof(Damageable));
        }

        private void ValidateFish()
        {
            Check(false);
            if (!_capture.TryResolveNativeFish(_target.SceneEpoch, _target.EntityId, out FishAISystem current) || Read(() => Pointer(current)) != _fishPointer)
                throw new Rejected("FishBindingChanged");
            HostEntityTarget? target = _capture.ResolveObservedPointer(_fishPointer.ToInt64());
            if (!target.HasValue || !target.Value.Equals(_target) || Read(() => _fish.m_CachedPtr) != _fishUnity ||
                Read(() => IL2CPP.il2cpp_object_get_class(_fishPointer)) != _fishClass || Read(() => _root.m_CachedPtr) != _rootUnity ||
                Read(() => Pointer(_fish.gameObject)) != _rootPointer || !Read(() => _root.activeInHierarchy) ||
                Read(() => _root.scene.handle) != _before.SceneHandle || Read(() => Pointer(_fish._FishInfoData)) != _infoPointer ||
                Read(() => Pointer(_fish._fishInteractionBody)) != _bodyPointer || Read(() => Pointer(_body._ownerFish)) != _fishPointer ||
                Read(() => Pointer(_fish._fishDamageable)) != Read(() => Pointer(_damageable)) ||
                (!ReferenceEquals(_special, null) && (Read(() => Pointer(_special)) != _fishPointer ||
                    Read(() => Pointer(_special._IsDeadRP_k__BackingField)) != Read(() => Pointer(_dead)))) ||
                Read(() => Pointer(_fish.IsCorpseRP)) != Read(() => Pointer(_corpse)) ||
                Read(() => Pointer(_fish._isFishCapturedRP)) != Read(() => Pointer(_captured)) ||
                Read(() => Pointer(_fish.IsHookedRP)) != Read(() => Pointer(_hooked)) ||
                Read(() => _info._TID_k__BackingField) != _target.DataTid || Read(() => _body.InteractionType) != FishInteractionBody.FishInteractionType.Pickup ||
                (!ReferenceEquals(_dead, null) && !Read(() => _dead.value)) || !Read(() => _damageable.m_IsDead) || Read(() => _corpse.value) || Read(() => _captured.value) ||
                Read(() => _hooked.value) || Read(() => _fish._IsFishHookedSequence_k__BackingField) || !ReferenceEquals(Read(() => _fish._FishJoint_k__BackingField), null) ||
                !ReferenceEquals(Read(() => _fish._m_fishmonBody_k__BackingField), null) || Read(() => _fish._IsFishSleeped_k__BackingField) ||
                Read(() => _info._DLCType_k__BackingField) != 0 || Read(() => _info._FishCollectionNotAvailable_k__BackingField) ||
                !Read(() => _info._IsIncludeCollectionProgress_k__BackingField))
                throw new Rejected("DownedFishProfileChanged");
            int collectionTid = Read(() => _info._FishCollectionFishTID_k__BackingField);
            if (collectionTid > 0 && collectionTid != _target.DataTid) throw new Rejected("CollectionProfileChanged");
            if (!_command.TryReadPosition(out NVector3 actor)) throw new Rejected("ActorPositionUnavailable");
            Transform transform = Read(() => _fish.transform); Vector3 p = Read(() => transform.position);
            double dx = (double)p.x - actor.X, dy = (double)p.y - actor.Y;
            if (!Finite(actor) || !float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) ||
                Math.Abs((double)p.z - actor.Z) > 0.01 || dx * dx + dy * dy > InteractionDistance * InteractionDistance)
                throw new Rejected("InteractionRangeChanged");
        }

        private void ValidateTerminal()
        {
            Check(false);
            if (Read(() => Pointer(_fish)) != _fishPointer || Read(() => _fish.m_CachedPtr) != _fishUnity ||
                Read(() => IL2CPP.il2cpp_object_get_class(_fishPointer)) != _fishClass || Read(() => Pointer(_fish.gameObject)) != _rootPointer ||
                Read(() => _root.m_CachedPtr) != _rootUnity || Read(() => _root.activeSelf) || Read(() => _root.activeInHierarchy) ||
                Read(() => Pointer(_fish.IsCorpseRP)) != Read(() => Pointer(_corpse)) || !Read(() => _corpse.value) ||
                _lifecycle.Tracker.TryGetActiveGeneration(_fishPointer.ToInt64(), out _))
                throw new Rejected("TerminalReadbackUnavailable");
            TerminalReadbackObserved = true;
        }

        private void Progress(Action action)
        {
            ValidateFish(); ValidateHeld(); RequireUnchangedBag(); ProgressCallsEntered++; Status = "ProgressEnteredUnknown";
            action(); ProgressCallsReturned++;
            Check(false); RequireUnchangedBag(); ValidateFish();
        }

        private CargoMemberSnapshot Employee()
        {
            CargoLedgerSnapshot snapshot = _command.Binding.Ledger.Snapshot;
            if (snapshot.Phase != CargoExpeditionPhase.Active || snapshot.ExpeditionId != _command.Binding.ExpeditionId || snapshot.SourceRoomId != _command.RoomId)
                throw new Rejected("ExpeditionRetired");
            foreach (CargoMemberSnapshot member in snapshot.Members)
                if (member.MemberId == _command.MemberId && member.BagMode == CargoBagMode.EmployeeVirtual && member.Connected) return member;
            throw new Rejected("EmployeeBagUnavailable");
        }
        private void RequireProgressBudget()
        {
            var looting = Read(() => _save.m_LootingData); var caught = Read(() => _save.m_CaughtFishData);
            long lootCount = Read(() => looting._count), caughtCount = Read(() => caught._count);
            // A conservative bound for this file's own Check steps, including
            // worst eight selected products and record/class/int readbacks.
            // Composite source/command getter internals are not a CPU/read cap.
            long estimate = 16384 + 32 * (lootCount + CargoValues.MaxProductsPerCapture) * (CargoValues.MaxProductsPerCapture + 1) +
                40 * (caughtCount + 1) * 3;
            if (estimate > MaxSteps) throw new Rejected("ProgressWorkProfileUnsupported");
        }
        private void ValidateHeld()
        {
            foreach (Reference held in _references)
                if (Read(() => Pointer(held.Wrapper)) != held.Pointer || held.Handle == IntPtr.Zero ||
                    Read(() => IL2CPP.il2cpp_gchandle_get_target(held.Handle)) != held.Pointer)
                    throw new Rejected("HeldNativeReferenceChanged");
            if (Read(() => Pointer(Singleton<MissionManager>._instance)) != _missionPointer || Read(() => Pointer(_mission)) != _missionPointer)
                throw new Rejected("MissionOwnerChanged");
        }

        private CargoSourceFacts SourceFacts()
        {
            Check(false); ValidateFish(); RequireUnchangedBag(); CargoMemberSnapshot member = Employee();
            CargoSource source = _intent?.Source ?? new CargoSource { RoomId = _command.RoomId, SceneEpoch = _target.SceneEpoch, EntityId = _target.EntityId, LocalGeneration = _target.Generation };
            return new CargoSourceFacts
            {
                ExpeditionId = _command.Binding.ExpeditionId, MemberId = _command.MemberId, BoundPlayerId = 2,
                RequestId = _command.InputSequence, OperationId = _lease?.OperationId ?? 0, IntentFingerprint = _lease?.IntentFingerprint,
                CurrentRoomId = _command.RoomId, Source = CargoValues.Copy(source), BagRevision = member.BagRevision,
                ActorRevision = _command.ActorRevision, LoadoutRevision = _command.LoadoutRevision, SampledAt = _command.Now,
                HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true, SourceAvailable = true,
                CapacityPolicyVerified = true, CapacityPolicy = _command.CapacityPolicy, CapacityRoutingVerified = true,
                NativeEntryCapabilityVerified = _fenced && HooksHealthy(), YieldSelectionIsolationVerified = _fenced && HooksHealthy(),
                NativeCurrentWeight = _before.WeightKg
            };
        }
        private CargoLateYieldFacts LateFacts()
        {
            CargoSourceFacts f = SourceFacts();
            return new CargoLateYieldFacts
            {
                ExpeditionId = f.ExpeditionId, MemberId = f.MemberId, BoundPlayerId = f.BoundPlayerId, RequestId = f.RequestId,
                OperationId = f.OperationId, IntentFingerprint = f.IntentFingerprint, CurrentRoomId = f.CurrentRoomId, Source = f.Source,
                BagRevision = f.BagRevision, ActorRevision = f.ActorRevision, LoadoutRevision = f.LoadoutRevision, SampledAt = f.SampledAt,
                HostAuthority = f.HostAuthority, SourceIdentityVerified = f.SourceIdentityVerified, OrdinaryFishVerified = f.OrdinaryFishVerified,
                ActorPermitted = f.ActorPermitted, SourceAvailable = f.SourceAvailable, CapacityPolicyVerified = f.CapacityPolicyVerified,
                CapacityPolicy = f.CapacityPolicy, CapacityRoutingVerified = f.CapacityRoutingVerified,
                NativeEntryCapabilityVerified = f.NativeEntryCapabilityVerified, YieldSelectionIsolationVerified = f.YieldSelectionIsolationVerified,
                NativeCurrentWeight = f.NativeCurrentWeight, ProductsFingerprint = CargoValues.ProductsFingerprint(_products),
                CompleteSelectedYield = true, MaterializationBoundaryHeld = true, NoBagWriteYet = true
            };
        }
        private CargoCaptureFacts ReceiptFacts()
        {
            Check(false); RequireUnchangedBag(); ValidateTerminal(); CargoMemberSnapshot member = Employee();
            return new CargoCaptureFacts
            {
                ExpeditionId = _command.Binding.ExpeditionId, MemberId = _command.MemberId, BoundPlayerId = 2,
                RequestId = _command.InputSequence, OperationId = _lease.OperationId, ProductsFingerprint = CargoValues.ProductsFingerprint(_products),
                CurrentRoomId = _command.RoomId, Source = CargoValues.Copy(_lease.Intent.Source), BagRevision = member.BagRevision, SampledAt = _command.Now,
                HostAuthority = true, SourceIdentityVerified = true, OrdinaryFishVerified = true, ActorPermitted = true,
                CapacityPolicyVerified = true, CapacityPolicy = _command.CapacityPolicy, CapacityRoutingVerified = true,
                YieldVerified = true, CaptureTerminal = TerminalReadbackObserved && _terminalReturned,
                DiversionVerified = true, NoHostBagWrite = !_bagWriteAttempted && NoHostBagWriteObserved,
                NativeCurrentWeight = _before.WeightKg
            };
        }

        private bool ReadLooting(int key, bool requireKey)
        {
            var dictionary = Read(() => _save.m_LootingData);
            if (ReferenceEquals(dictionary, null)) throw new Rejected("LootingContainerUnavailable");
            int count = Read(() => dictionary._count), free = Read(() => dictionary._freeCount), version = Read(() => dictionary._version);
            var entries = Read(() => dictionary._entries); int length = ReferenceEquals(entries, null) ? 0 : Read(() => entries.Length);
            if (count < 0 || free < 0 || free > count || count > length || length > MaxProgressEntries) throw new Rejected("LootingContainerUnsupported");
            bool found = false; int active = 0; var keys = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int index = i; var entry = Read(() => entries[index]);
                if (Read(() => entry.hashCode) < 0) continue;
                active++; int actualKey = Read(() => entry.key); var value = Read(() => entry.value);
                if (!keys.Add(actualKey) || ReferenceEquals(value, null)) throw new Rejected("LootingEntryUnsupported");
                RequireClass(value, typeof(LootingSave));
                int actualId = Decode(Read(() => value.m_LootID));
                if (actualId != actualKey) throw new Rejected("LootingKeyMismatch");
                if (actualKey == key) found = true;
            }
            if (active != count - free || Read(() => dictionary._count) != count || Read(() => dictionary._freeCount) != free ||
                Read(() => dictionary._version) != version || Read(() => Pointer(dictionary._entries)) != Pointer(entries) ||
                Read(() => Pointer(_save.m_LootingData)) != Pointer(dictionary)) throw new Rejected("LootingContainerChanged");
            if (requireKey && !found) throw new Rejected("LootingEntryMissing"); return found;
        }
        private int? ReadCaught(int key, bool requireKey)
        {
            var dictionary = Read(() => _save.m_CaughtFishData);
            if (ReferenceEquals(dictionary, null)) throw new Rejected("CodexContainerUnavailable");
            int count = Read(() => dictionary._count), free = Read(() => dictionary._freeCount), version = Read(() => dictionary._version);
            var entries = Read(() => dictionary._entries); int length = ReferenceEquals(entries, null) ? 0 : Read(() => entries.Length);
            if (count < 0 || free < 0 || free > count || count > length || length > MaxProgressEntries) throw new Rejected("CodexContainerUnsupported");
            int? found = null; int active = 0; var keys = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int index = i; var entry = Read(() => entries[index]);
                if (Read(() => entry.hashCode) < 0) continue;
                active++; int actualKey = Read(() => entry.key); var value = Read(() => entry.value);
                if (!keys.Add(actualKey) || ReferenceEquals(value, null)) throw new Rejected("CodexEntryUnsupported");
                RequireClass(value, typeof(CaughtFishSave));
                int actualId = Decode(Read(() => value.fishID)), grade = Decode(Read(() => value.grade));
                if (actualId != actualKey || grade < 1 || grade > 1000) throw new Rejected("CodexEntryUnsupported");
                if (actualKey == key) found = grade;
            }
            if (active != count - free || Read(() => dictionary._count) != count || Read(() => dictionary._freeCount) != free ||
                Read(() => dictionary._version) != version || Read(() => Pointer(dictionary._entries)) != Pointer(entries) ||
                Read(() => Pointer(_save.m_CaughtFishData)) != Pointer(dictionary)) throw new Rejected("CodexContainerChanged");
            if (requireKey && !found.HasValue) throw new Rejected("CodexEntryMissing"); return found;
        }
        private static int Decode(CodeStage.AntiCheat.ObscuredTypes.ObscuredInt value)
        {
            var result = LootSlotSnapshot.DecodeInt(new LootObscuredIntSnapshot(value.currentCryptoKey, value.hiddenValue,
                value.inited, value.fakeValue, value.fakeValueActive));
            if (!result.Available) throw new Rejected("ProgressIntDecodeUnavailable"); return result.Value.Value;
        }

        private void RequireUnchangedBag()
        {
            Check(false);
            if (_before == null || !_host.TryConfirmUnchanged(_before, out NativeHostCargoSnapshot current) ||
                !_host.TryReadCommitRoots(current, out SaveData save, out LootBox bag) ||
                Pointer(save) != _savePointer || Pointer(bag) != _bagPointer) throw new Rejected("HostBagOrRootsChanged");
            _before = current; NoHostBagWriteObserved = !_bagWriteAttempted;
            Check(false);
        }
        private void Check(bool requireFence)
        {
            if (Environment.CurrentManagedThreadId != _thread || _failed || _stopped || _checking || ++_steps > MaxSteps || _hookFailure)
                throw new Rejected("ReadWindowUnavailable");
            _checking = true;
            try
            {
                if (!_command.IsCurrent() || !_lifecycle.OwnsActiveTracker || !_capture.UsesLifecycle(_lifecycle.Tracker) ||
                    (requireFence && (!_fenced || !HooksHealthy())) || (_before != null && !_host.IsSameActiveDive(_before)))
                    throw new Rejected("SourceWindowRetired");
                if (_failed || _stopped) throw new Rejected("ReentrantSourceLost");
            }
            finally { _checking = false; }
        }
        private T Read<T>(Func<T> read) { Check(false); T value = read(); Check(false); return value; }
        private void RequireClass(Il2CppObjectBase value, Type expected)
        {
            IntPtr pointer = Read(() => Pointer(value)); if (pointer == IntPtr.Zero) throw new Rejected("NullNativeReference");
            IntPtr expectedClass = Read(() => ExpectedClass(expected));
            if (expectedClass == IntPtr.Zero || Read(() => IL2CPP.il2cpp_object_get_class(pointer)) != expectedClass)
                throw new Rejected("UnsupportedExactNativeClass");
        }
        private void Keep(Il2CppObjectBase wrapper)
        {
            IntPtr pointer = Read(() => Pointer(wrapper)); if (pointer == IntPtr.Zero) throw new Rejected("AbsentStrongReference");
            foreach (Reference known in _references) if (known.Pointer == pointer) return;
            if (_references.Count >= MaxReferences) throw new Rejected("StrongReferenceQuota");
            var item = new Reference { Wrapper = wrapper, Pointer = pointer }; _references.Add(item);
            Check(false); item.Handle = IL2CPP.il2cpp_gchandle_new(pointer, false); Check(false);
            if (item.Handle == IntPtr.Zero || Read(() => IL2CPP.il2cpp_gchandle_get_target(item.Handle)) != pointer)
                throw new Rejected("StrongReferenceUnknown");
        }
        private void Fail(string reason) { _failed = true; Status = reason; }
        private static bool Finite(NVector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
            Math.Abs(value.X) <= 100000 && Math.Abs(value.Y) <= 100000 && Math.Abs(value.Z) <= 100000;
        private static IntPtr Pointer(Il2CppObjectBase value) => ReferenceEquals(value, null) ? IntPtr.Zero : value.Pointer;
        private static IntPtr ExpectedClass(Type type)
        {
            if (type == typeof(SaveData)) return Il2CppClassPointerStore<SaveData>.NativeClassPtr;
            if (type == typeof(LootBox)) return Il2CppClassPointerStore<LootBox>.NativeClassPtr;
            if (type == typeof(MissionManager)) return Il2CppClassPointerStore<MissionManager>.NativeClassPtr;
            if (type == typeof(DR.FishInfoData)) return Il2CppClassPointerStore<DR.FishInfoData>.NativeClassPtr;
            if (type == typeof(FishInteractionBody)) return Il2CppClassPointerStore<FishInteractionBody>.NativeClassPtr;
            if (type == typeof(Damageable)) return Il2CppClassPointerStore<Damageable>.NativeClassPtr;
            if (type == typeof(LootingSave)) return Il2CppClassPointerStore<LootingSave>.NativeClassPtr;
            if (type == typeof(CaughtFishSave)) return Il2CppClassPointerStore<CaughtFishSave>.NativeClassPtr;
            return IntPtr.Zero;
        }

        private static void EnsureHooks()
        {
            if (_hookFailure) throw new Rejected("CaptureFencePreviouslyFailed");
            if (_harmony != null) { if (!HooksHealthy()) throw new Rejected("CaptureFenceLost"); return; }
            var targets = new List<(MethodInfo Method, MethodInfo Prefix, MethodInfo Postfix, MethodInfo Finalizer)>();
            void Add(Type type, string name, Type result, Type[] args, string prefix, string postfix = null, string finalizer = null)
            {
                MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, args, null);
                if (method == null || method.IsStatic || method.ReturnType != result) throw new Rejected("CaptureFenceSignatureMissing");
                targets.Add((method, typeof(NativeEmployeeCaptureCommitBridge).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static),
                    postfix == null ? null : typeof(NativeEmployeeCaptureCommitBridge).GetMethod(postfix, BindingFlags.NonPublic | BindingFlags.Static),
                    finalizer == null ? null : typeof(NativeEmployeeCaptureCommitBridge).GetMethod(finalizer, BindingFlags.NonPublic | BindingFlags.Static)));
            }
            Type lifted = typeof(LootBox.AutoLiftedType), times = typeof(Il2CppSystem.Collections.Generic.List<string>);
            Add(typeof(FishAISystem), "OnSuccessPickUp", typeof(void), Type.EmptyTypes, nameof(FishFence));
            Add(typeof(FishAISystem), "SuccessPickupFish", typeof(void), new[] { typeof(int), typeof(bool) }, nameof(FishFence));
            Add(typeof(FishAISystem), "LootDeadFishBody", typeof(void), Type.EmptyTypes, nameof(FishFence));
            Add(typeof(FishAISystem), "AddDropItem_Impl", typeof(void), new[] { typeof(int), lifted, typeof(int), typeof(bool) }, nameof(FishFence));
            Add(typeof(FishAISystem), "AddDropPlusItem_Impl", typeof(void), new[] { typeof(int), lifted }, nameof(FishFence));
            Add(typeof(FishAISystem), "AddDropItemLootBoxWithPlus", typeof(void), new[] { typeof(int), lifted, typeof(int) }, nameof(FishFence));
            Add(typeof(FishAISystem), "WinFromProjectileinFight", typeof(void), Type.EmptyTypes, nameof(FishFence));
            Add(typeof(FishAISystem), "SuccessNetPickupFish", typeof(void), new[] { typeof(bool) }, nameof(FishFence));
            Add(typeof(FishAISystem), "DestroySelf", typeof(void), Type.EmptyTypes, nameof(DestroyBefore), nameof(DestroyAfter), nameof(DestroyFinally));
            Add(typeof(FishInteractionBody), "SuccessInteract", typeof(void), new[] { typeof(BaseCharacter) }, nameof(BodyFence));
            Add(typeof(LootBox), "Add", typeof(bool), new[] { typeof(int), typeof(int), typeof(int), lifted, times, typeof(bool) }, nameof(BagBoolFence));
            Add(typeof(LootBox), "AddIgnoreOverloaded", typeof(bool), new[] { typeof(int), typeof(int), typeof(int), lifted, times, typeof(bool) }, nameof(BagBoolFence));
            Add(typeof(LootBox), "Add_Impl", typeof(void), new[] { typeof(DR.IItemBase), typeof(int), typeof(int), lifted, times, typeof(bool) }, nameof(BagFence));
            Add(typeof(SaveData), "AddLootBox", typeof(void), new[] { typeof(SaveData.LootBoxType), typeof(string), typeof(LootBoxSlot) }, nameof(SaveBagFence));
            var harmony = new Harmony(HookOwner); _harmony = harmony; _hookMethods = new List<MethodInfo>();
            try
            {
                foreach (var target in targets)
                {
                    _hookMethods.Add(target.Method);
                    harmony.Patch(target.Method, prefix: new HarmonyMethod(target.Prefix),
                        postfix: target.Postfix == null ? null : new HarmonyMethod(target.Postfix),
                        finalizer: target.Finalizer == null ? null : new HarmonyMethod(target.Finalizer));
                    var patch = Harmony.GetPatchInfo(target.Method);
                    if (patch == null || !patch.Owners.Contains(HookOwner)) throw new Rejected("CaptureFenceInstallUnknown");
                }
                _harmony = harmony;
            }
            catch
            {
                _hookFailure = true;
                try { harmony.UnpatchSelf(); } catch { }
                throw;
            }
        }
        private static bool HooksHealthy()
        {
            if (_hookFailure || _harmony == null || _hookMethods == null) return false;
            foreach (MethodInfo method in _hookMethods)
            { var patch = Harmony.GetPatchInfo(method); if (patch == null || !patch.Owners.Contains(HookOwner)) return false; }
            return true;
        }
        private static NativeEmployeeCaptureCommitBridge ReservedFish(FishAISystem fish)
        {
            if (ReferenceEquals(fish, null)) return null;
            // Pointer is a framework native GC-handle lookup. A worker may
            // only match the exact already-retained CLR wrapper, never read it.
            int thread = Environment.CurrentManagedThreadId;
            NativeEmployeeCaptureCommitBridge[] owners;
            lock (OwnershipSync) owners = Owners.ToArray();
            foreach (var candidate in owners)
            {
                if (!candidate._fenced || thread == candidate._thread) continue;
                if (!ReferenceEquals(fish, candidate._fish)) continue;
                candidate.Fail("UnsupportedWorkerFishCallback"); return candidate;
            }
            var executing = _executing;
            if (executing != null && thread != executing._thread)
            {
                executing.Fail("UnboundWorkerFishCallback");
                return null; // Unknown wrapper is not evidence about this fish.
            }
            foreach (var candidate in owners)
            {
                if (!candidate._fenced || thread != candidate._thread) continue;
                try
                {
                    if (Pointer(fish) != candidate._fishPointer) continue;
                    if (candidate.IsProvenLaterBirth(fish)) continue;
                    return candidate;
                }
                catch { candidate.Fail("CaptureFenceCallbackUnknown"); return candidate; }
            }
            return null;
        }
        private static bool FishFence(FishAISystem __instance)
        {
            var owner = ReservedFish(__instance);
            if (owner == null) return true;
            if (ReferenceEquals(_executing, owner)) owner.Fail("UnexpectedNativePickupDuringHarvest");
            return false;
        }
        private static bool DestroyBefore(FishAISystem __instance, bool __runOriginal)
        {
            var owner = ReservedFish(__instance); if (owner == null) return true;
            if (Environment.CurrentManagedThreadId != owner._thread) return false;
            if (ReferenceEquals(_executing, owner) && owner._terminalEntered && owner._terminalPermit && __runOriginal && !owner._terminalReturned)
            { owner._terminalPermit = false; return true; }
            owner.Fail("UnexpectedFishRetirement"); return false;
        }
        private static void DestroyAfter(FishAISystem __instance, bool __runOriginal)
        {
            var owner = _executing;
            if (owner == null || Environment.CurrentManagedThreadId != owner._thread || !owner._terminalEntered) return;
            try
            {
                if (Pointer(__instance) != owner._fishPointer) return;
                if (!__runOriginal) owner.Fail("OriginalTerminalWasSkipped");
                else owner._terminalReturned = true;
            }
            catch { owner.Fail("TerminalCallbackUnknown"); }
        }
        private static Exception DestroyFinally(Exception __exception)
        {
            // No Unity/native read, no result substitution and no exception
            // suppression. Even an exception after the postfix invalidates the
            // pending receipt before the caller resumes.
            var owner = _executing;
            if (owner != null && owner._terminalEntered && __exception != null) owner.Fail("OriginalTerminalException");
            return __exception;
        }
        private bool IsProvenLaterBirth(FishAISystem fish)
        {
            if (!_lifecycle.OwnsActiveTracker || !_capture.UsesLifecycle(_lifecycle.Tracker) ||
                !_lifecycle.Tracker.TryGetActiveGeneration(_fishPointer.ToInt64(), out long generation) || generation <= _target.Generation) return false;
            HostEntityTarget? fresh = _capture.ResolveObservedPointer(_fishPointer.ToInt64());
            if (!fresh.HasValue || fresh.Value.Generation != generation || fresh.Value.Kind != EntityKind.Fish ||
                !_capture.TryResolveNativeFish(fresh.Value.SceneEpoch, fresh.Value.EntityId, out FishAISystem actual) || Pointer(actual) != Pointer(fish)) return false;
            return _lifecycle.Tracker.TryGetActiveGeneration(_fishPointer.ToInt64(), out long after) && after == generation;
        }
        private static bool BodyFence(FishInteractionBody __instance)
        {
            if (ReferenceEquals(__instance, null)) return true;
            int thread = Environment.CurrentManagedThreadId;
            NativeEmployeeCaptureCommitBridge[] owners;
            lock (OwnershipSync) owners = Owners.ToArray();
            foreach (var owner in owners)
            {
                if (!owner._fenced || thread == owner._thread || !ReferenceEquals(__instance, owner._body)) continue;
                owner.Fail("UnsupportedWorkerBodyCallback"); return false;
            }
            var executing = _executing;
            if (executing != null && thread != executing._thread)
            { executing.Fail("UnboundWorkerBodyCallback"); return true; }
            foreach (var owner in owners)
            {
                if (!owner._fenced || thread != owner._thread) continue;
                try
                {
                    if (Pointer(__instance) != owner._bodyPointer) continue;
                    if (owner.IsProvenLaterBirth(owner._fish)) continue;
                    if (ReferenceEquals(_executing, owner)) owner.Fail("UnexpectedNativeBodyPickupDuringHarvest"); return false;
                }
                catch { owner.Fail("BodyFenceCallbackUnknown"); return false; }
            }
            return true;
        }
        private static bool BagFence(LootBox __instance) => BagWrite(__instance, false);
        private static bool BagBoolFence(LootBox __instance, ref bool __result)
        {
            bool allow = BagWrite(__instance, false);
            if (!allow) __result = false; // Never report successful rejected materialization.
            return allow;
        }
        private static bool SaveBagFence(SaveData __instance) => BagWrite(__instance, true);
        private static bool BagWrite(Il2CppObjectBase instance, bool save)
        {
            var owner = _executing; if (owner == null || ReferenceEquals(instance, null)) return true;
            if (Environment.CurrentManagedThreadId != owner._thread)
            {
                bool sameWrapper = ReferenceEquals(instance, save ? (Il2CppObjectBase)owner._save : owner._bag);
                if (sameWrapper) owner._bagWriteAttempted = true;
                owner.Fail(sameWrapper ? "UnsupportedWorkerBagWrite" : "UnboundWorkerBagCallback");
                return !sameWrapper; // Unknown identity is not a host-bag proof.
            }
            try
            {
                if (Pointer(instance) != (save ? owner._savePointer : owner._bagPointer)) return true;
                owner._bagWriteAttempted = true; owner.Fail("HostBagWriteAttemptedDuringHarvest"); return false;
            }
            catch { owner.Fail("BagFenceCallbackUnknown"); return false; }
        }
    }

    // This accessor lives with the new consumer. It cannot mint a resource or
    // accept a foreign lease; all returned wrappers already belong to the
    // existing native selection owner and its completed cached normalization.
    internal sealed partial class NativeEmployeeFishSelectionBridge
    {
        internal bool TryReadHarvestSelection(ExpeditionCargoLedger ledger, CargoSourceLease lease,
            out FishAISystem fish, out DR.FishInfoData info, out FishInteractionBody body,
            out CargoProduct[] products, out DR.IItemBase[] items, out int bonus)
        {
            fish = null; info = null; body = null; products = null; items = null; bonus = 0;
            if (!ReferenceEquals(ledger, _ledger) || !ReferenceEquals(lease, _lease) || !_prepared || _released || _releaseAttempted ||
                _recipe.Kind != FishYieldRecipeKind.OrdinaryPickup || _selection == null || !_ledger.IsSelectionEntered(_lease)) return false;
            ValidateSource();
            FishProductNormalizationSnapshot normalized = _selection.Normalization;
            FishYieldSelectionSnapshot selected = _selection.Snapshot;
            FishReturnMappingSnapshot mapping = _selection.ReturnMapping;
            if (selected.Stage != FishYieldSelectionStage.RawPlanHeld || !selected.PlusSelectionCompleted || !selected.PickupBonusGrade.HasValue ||
                normalized.Stage != FishProductNormalizationStage.CaptureProductsHeld || mapping.Stage != FishReturnMappingStage.MappingReady ||
                normalized.ProductsFingerprint != mapping.RawProductsFingerprint || normalized.Products.Length != mapping.Results.Length ||
                normalized.Products.Length != _resources.Count || normalized.Products.Length < 1) return false;
            var ownedItems = new DR.IItemBase[_resources.Count];
            for (int i = 0; i < ownedItems.Length; i++)
            {
                HeldResource held = _resources[i]; FishProductSample sample = normalized.Samples[i];
                if (sample.Ordinal != held.Drop.Ordinal || sample.SelectedLookupId != held.Drop.ItemId ||
                    sample.ProductTid != sample.SelectedLookupId ||
                    sample.ProductTid != normalized.Products[i].ProductId || sample.CaptureGrade != normalized.Products[i].Grade ||
                    held.Pointer == IntPtr.Zero || Pointer(held.Item) != held.Pointer) return false;
                ownedItems[i] = held.Item;
            }
            ValidateSource(); fish = _fish; info = _info; body = _body; bonus = selected.PickupBonusGrade.Value;
            products = CargoValues.CopyProducts(normalized.Products); items = ownedItems; return true;
        }
    }
}
