using System;
using System.Collections.Generic;
using System.Linq;

namespace DaveCoop.Core.Cargo
{
    // Host-owned, caller-serialized CLR evidence. Lifetime is one expedition,
    // not a socket/scene. No method calls native code or grants native permission.
    // Legacy Reserve requires an already verified complete yield plan. The
    // source-only path binds products after an isolated selection mark; unknown
    // native random yields are never guessed, rerolled or inferred from a TID.
    public sealed class ExpeditionCargoLedger
    {
        private sealed class Member
        {
            public CargoMemberSetup Setup;
            public double Weight;
            public double Reserved;
            public double NativeWeightSampledAt = -1;
            public long Revision = 1;
            public long HighestRequest;
            public bool Connected = true;
            public readonly Dictionary<long, long> Requests = new Dictionary<long, long>();
            public int PlayerId => Setup.BagMode == CargoBagMode.HostNative ? 1 : 2;
        }
        private sealed class Capture
        {
            public long Id;
            public CargoSourceIntent Intent;
            public CargoSourceLease SourceLease;
            public bool SelectionIsolationEntered;
            public CargoProduct[] SelectedProducts;
            public CargoCaptureRequest Request;
            public string Fingerprint;
            public string ProductsFingerprint;
            public string SourceKey;
            public double Weight;
            public CargoCaptureStage Stage = CargoCaptureStage.Reserved;
            public CargoReceiptKind? ReceiptKind;
        }
        private sealed class ReturnItem
        {
            public Capture Capture;
            public int Index;
            public CargoReturnStage Stage = CargoReturnStage.Unclaimed;
        }
        private readonly string _expedition;
        private readonly Dictionary<string, Member> _members = new Dictionary<string, Member>(StringComparer.Ordinal);
        private readonly Dictionary<long, Capture> _captures = new Dictionary<long, Capture>();
        private readonly Dictionary<long, long> _operations = new Dictionary<long, long>();
        private readonly Dictionary<string, long> _sourceLeases = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<(long Capture, int Index), ReturnItem> _returnItems = new Dictionary<(long, int), ReturnItem>();
        private CargoExpeditionPhase _phase = CargoExpeditionPhase.Active;
        private string _returnId;
        private string _sourceRoom;
        private double _lastNow = -1;
        private long _operationHighWater;
        public long GlobalOperationHighWater => _operationHighWater;

        // Exact-object query for a host-local backend's preparation. This does
        // not grant native entry; SelectOnce must still use EnterSelection.
        internal bool IsSelectionReserved(CargoSourceLease lease) =>
            TrySourceLease(lease, out Capture capture, out _) &&
            capture.Stage == CargoCaptureStage.Reserved && capture.Request == null;

        internal bool IsSelectionEntered(CargoSourceLease lease) =>
            TrySourceLease(lease, out Capture capture, out _) && capture.SelectionIsolationEntered &&
            capture.Stage == CargoCaptureStage.EnteredUnknown;

        public ExpeditionCargoLedger(string expeditionId, CargoMemberSetup[] members)
        {
            _expedition = CargoValues.GuidKey(expeditionId);
            if (members == null || members.Length != CargoValues.MaxMembers) throw new ArgumentException("Cargo requires two members.");
            // Prepare and validate both members before mutating any collection.
            var owned = new List<CargoMemberSetup>(CargoValues.MaxMembers);
            foreach (CargoMemberSetup member in members)
            {
                if (member == null || !Enum.IsDefined(typeof(CargoBagMode), member.BagMode) || member.Capacity <= 0)
                    throw new ArgumentException("Invalid cargo member.");
                CargoValues.Weight(member.Capacity); CargoValues.Weight(member.InitialWeight);
                if (member.BagMode == CargoBagMode.EmployeeVirtual && member.InitialWeight != 0)
                    throw new ArgumentException("Employee virtual bags start empty.");
                owned.Add(new CargoMemberSetup { MemberId = CargoValues.GuidKey(member.MemberId), BagMode = member.BagMode, Capacity = member.Capacity, InitialWeight = member.InitialWeight });
            }
            if (owned.Select(item => item.MemberId).Distinct(StringComparer.Ordinal).Count() != 2 ||
                owned.Select(item => item.BagMode).Distinct().Count() != 2) throw new ArgumentException("Cargo needs distinct host and employee identities.");
            foreach (CargoMemberSetup setup in owned) _members.Add(setup.MemberId, new Member { Setup = setup, Weight = setup.InitialWeight });
        }

        public CargoLedgerSnapshot Snapshot => new CargoLedgerSnapshot
        {
            ExpeditionId = _expedition, ReturnId = _returnId, Phase = _phase, SourceRoomId = _sourceRoom,
            Members = _members.Values.OrderBy(member => member.PlayerId).Select(MemberSnapshot).ToArray(),
            Captures = _captures.Values.OrderBy(capture => capture.Id).Select(capture => new CargoCaptureSnapshot
            {
                CaptureId = capture.Id, Intent = capture.Intent == null ? null : CargoSourceValues.Copy(capture.Intent),
                Request = capture.Request == null ? null : CargoValues.Copy(capture.Request), YieldBound = capture.Request != null,
                SelectedYieldKnown = capture.SelectedProducts != null,
                SelectedYieldFingerprint = capture.SelectedProducts == null ? null : capture.ProductsFingerprint,
                OperationId = CaptureOperation(capture), Fingerprint = capture.Fingerprint,
                Stage = capture.Stage, ReceiptKind = capture.ReceiptKind
            }).ToArray(),
            ReturnItems = _returnItems.Values.OrderBy(item => item.Capture.Id).ThenBy(item => item.Index).Select(item => new CargoReturnItemSnapshot
            {
                CaptureId = item.Capture.Id, ProductIndex = item.Index, MemberId = item.Capture.Request.MemberId,
                BagMode = _members[item.Capture.Request.MemberId].Setup.BagMode,
                Product = CargoValues.Copy(item.Capture.Request.Products[item.Index]), Stage = item.Stage
            }).ToArray()
        };

        public CargoResult SetConnected(string memberId, bool connected)
        {
            if (!TryMember(memberId, out Member member, out CargoReason failure)) return Result(failure);
            if (member.Connected == connected) return Result(CargoReason.Duplicate);
            member.Connected = connected;
            // Losing transport withdraws entry capability, not cargo, source
            // fences or a proof that an outstanding native call never entered.
            return Result(CargoReason.None);
        }

        public CargoResult Reserve(CargoCaptureRequest request, CargoCaptureFacts facts, double now)
        {
            CargoCaptureRequest owned; string fingerprint; string productsFingerprint; string sourceKey;
            try
            {
                owned = CargoValues.Copy(request); fingerprint = CargoValues.Fingerprint(owned);
                productsFingerprint = CargoValues.ProductsFingerprint(owned.Products); sourceKey = CargoValues.SourceKey(owned.Source);
            }
            catch (ArgumentException) { return Result(CargoReason.InvalidInput); }
            if (!TryMember(owned.MemberId, out Member member, out CargoReason failure)) return Result(failure);
            failure = CaptureFacts(member, owned, productsFingerprint, facts, now);
            if (failure != CargoReason.None) return Result(failure);
            failure = RoomFacts(facts);
            if (failure != CargoReason.None) return Result(failure);
            // Reconnection cannot assign a new source namespace and bypass an
            // old unknown fish lease. Verified native-source migration is not
            // implemented; expedition data itself still survives disconnection.
            if (_sourceRoom != null && owned.Source.RoomId != _sourceRoom) return Result(CargoReason.WrongIdentity);
            if (member.Requests.TryGetValue(owned.RequestId, out long previousId))
                return Result(_captures[previousId].Fingerprint == fingerprint ? CargoReason.Duplicate : CargoReason.Conflict, previousId);
            if (owned.RequestId <= member.HighestRequest) return Result(CargoReason.Replay);
            // A valid, host-bound new request consumes its member high water
            // even when capacity/source/quota business rules reject it.
            member.HighestRequest = owned.RequestId; _lastNow = now;
            if (_phase != CargoExpeditionPhase.Active) return Result(CargoReason.ReturnFrozen);
            failure = EntryFacts(member, facts);
            if (failure != CargoReason.None) return Result(failure);
            if (owned.BagRevision != member.Revision) return Result(CargoReason.Conflict);
            if (_operations.ContainsKey(owned.OperationId) || owned.OperationId <= _operationHighWater) return Result(CargoReason.Conflict);
            if (_sourceLeases.ContainsKey(sourceKey)) return Result(CargoReason.SourceBusy);
            // A network epoch can change without changing the physical fish.
            // Without a verified native-origin migration, no new epoch may
            // bypass an outstanding reservation/unknown call in the old one.
            if (_captures.Values.Any(item =>
                (item.Stage == CargoCaptureStage.Reserved || item.Stage == CargoCaptureStage.EnteredUnknown) &&
                CaptureSource(item).SceneEpoch != owned.Source.SceneEpoch)) return Result(CargoReason.SourceBusy);
            if (_captures.Count >= CargoValues.MaxCaptures || member.Revision == long.MaxValue) return Result(CargoReason.QuotaExceeded);
            double weight = CargoValues.ProductWeight(owned.Products);
            failure = Capacity(member, weight, facts.CapacityPolicy);
            if (failure != CargoReason.None) return Result(failure);
            long captureId = _captures.Count + 1;
            var capture = new Capture
            {
                Id = captureId, Request = owned, Fingerprint = fingerprint, ProductsFingerprint = productsFingerprint,
                SourceKey = sourceKey, Weight = weight
            };
            _captures.Add(captureId, capture); _operations.Add(owned.OperationId, captureId);
            _sourceLeases.Add(sourceKey, captureId); member.Requests.Add(owned.RequestId, captureId);
            _sourceRoom = owned.Source.RoomId;
            _operationHighWater = owned.OperationId;
            member.Reserved += weight; member.Revision++;
            RememberNativeWeightSample(member, facts);
            return Result(CargoReason.None, captureId);
        }

        // Reserve a member/source before the native generator chooses products.
        // Zero reserved weight means unknown yield, not an empty or safe bag.
        public CargoResult SourceReserve(CargoSourceIntent intent, CargoSourceFacts facts, double now, out CargoSourceLease lease)
        {
            lease = null; CargoSourceIntent owned; string fingerprint; string sourceKey;
            try { owned = CargoSourceValues.Copy(intent); fingerprint = CargoSourceValues.Fingerprint(owned); sourceKey = CargoValues.SourceKey(owned.Source); }
            catch (ArgumentException) { return Result(CargoReason.InvalidInput); }
            if (!TryMember(owned.MemberId, out Member member, out CargoReason failure)) return Result(failure);
            failure = SourceFacts(member, owned, facts, now);
            if (failure != CargoReason.None) return Result(failure);
            if (facts.OperationId != 0 || facts.IntentFingerprint != null) return Result(CargoReason.WrongIdentity);
            if (member.Requests.TryGetValue(owned.RequestId, out long previous))
            {
                Capture old = _captures[previous];
                if (old.SourceLease == null || old.Fingerprint != fingerprint) return Result(CargoReason.Conflict, previous);
                lease = old.SourceLease; return Result(CargoReason.Duplicate, previous);
            }
            if (owned.RequestId <= member.HighestRequest) return Result(CargoReason.Replay);
            member.HighestRequest = owned.RequestId; _lastNow = now;
            if (_phase != CargoExpeditionPhase.Active) return Result(CargoReason.ReturnFrozen);
            failure = SourceEntryFacts(member, facts, true);
            if (failure != CargoReason.None) return Result(failure);
            if (owned.BagRevision != member.Revision) return Result(CargoReason.Conflict);
            if (_sourceLeases.ContainsKey(sourceKey) || _captures.Values.Any(item =>
                (item.Stage == CargoCaptureStage.Reserved || item.Stage == CargoCaptureStage.EnteredUnknown) &&
                CaptureSource(item).SceneEpoch != owned.Source.SceneEpoch)) return Result(CargoReason.SourceBusy);
            if (_captures.Count >= CargoValues.MaxCaptures || member.Revision == long.MaxValue || _operationHighWater == long.MaxValue)
                return Result(CargoReason.QuotaExceeded);
            failure = Capacity(member, 0, facts.CapacityPolicy);
            if (failure != CargoReason.None) return Result(failure);
            long captureId = _captures.Count + 1, operation = _operationHighWater + 1;
            lease = new CargoSourceLease(captureId, operation, member.PlayerId, owned, fingerprint);
            var capture = new Capture { Id = captureId, Intent = owned, SourceLease = lease, Fingerprint = fingerprint, SourceKey = sourceKey };
            _captures.Add(captureId, capture); _operations.Add(operation, captureId); _sourceLeases.Add(sourceKey, captureId);
            member.Requests.Add(owned.RequestId, captureId); _sourceRoom = owned.Source.RoomId; _operationHighWater = operation;
            member.Revision++; RememberNativeWeightSample(member, facts); return Result(CargoReason.None, captureId);
        }

        // This one-way mark is BEFORE any native selection or pity mutation.
        // The adapter must already be able to hold all materialization, including
        // an ordinary Add that precedes a later plus-item roll in the game.
        public CargoResult EnterSelection(CargoSourceLease lease, CargoSourceFacts facts, double now)
        {
            if (!TrySourceLease(lease, out Capture capture, out CargoReason failure)) return Result(failure);
            if (_phase != CargoExpeditionPhase.Active) return Result(CargoReason.ReturnFrozen, capture.Id);
            if (capture.Stage != CargoCaptureStage.Reserved || capture.Request != null) return Result(CargoReason.InvalidStage, capture.Id);
            Member member = _members[capture.Intent.MemberId];
            failure = BoundSourceFacts(member, capture, facts, now);
            if (failure == CargoReason.None) failure = SourceEntryFacts(member, facts, true);
            if (failure == CargoReason.None && (!facts.NativeEntryCapabilityVerified || !facts.YieldSelectionIsolationVerified))
                failure = CargoReason.MissingCapability;
            if (failure == CargoReason.None) failure = Capacity(member, 0, facts.CapacityPolicy);
            if (failure != CargoReason.None) return Result(failure, capture.Id);
            capture.Stage = CargoCaptureStage.EnteredUnknown; capture.SelectionIsolationEntered = true;
            _lastNow = now; RememberNativeWeightSample(member, facts); return Result(CargoReason.None, capture.Id);
        }

        // Binding a complete already-selected yield is not native entry, a bag
        // commit or a capture receipt. Failed capacity/coverage leaves unknown.
        public CargoResult LateSeal(CargoSourceLease lease, CargoProduct[] selectedProducts, CargoLateYieldFacts facts, double now)
        {
            if (!TrySourceLease(lease, out Capture capture, out CargoReason failure)) return Result(failure);
            CargoProduct[] owned; string productsFingerprint;
            try { owned = CargoValues.CopyProducts(selectedProducts); productsFingerprint = CargoValues.ProductsFingerprint(owned); }
            catch (ArgumentException) { return Result(CargoReason.InvalidInput, capture.Id); }
            Member member = _members[capture.Intent.MemberId];
            failure = BoundSourceFacts(member, capture, facts, now);
            if (failure != CargoReason.None) return Result(failure, capture.Id);
            if (facts.ProductsFingerprint != productsFingerprint) return Result(CargoReason.WrongIdentity, capture.Id);
            if (capture.Request != null) return Result(capture.ProductsFingerprint == productsFingerprint ? CargoReason.Duplicate : CargoReason.Conflict, capture.Id);
            if (capture.SelectedProducts != null && capture.ProductsFingerprint != productsFingerprint)
                return Result(CargoReason.Conflict, capture.Id);
            if (capture.Stage != CargoCaptureStage.EnteredUnknown || !capture.SelectionIsolationEntered)
                return Result(CargoReason.InvalidStage, capture.Id);
            if (!facts.CompleteSelectedYield || !facts.MaterializationBoundaryHeld || !facts.NoBagWriteYet || !facts.YieldSelectionIsolationVerified)
                return Result(CargoReason.MissingCapability, capture.Id);
            // The native selector already chose this complete batch. Capacity
            // failure must not permit another roll, a lighter result or changed
            // quality under the same operation. No weight or receipt is added.
            if (capture.SelectedProducts == null)
            {
                capture.SelectedProducts = owned; capture.ProductsFingerprint = productsFingerprint; _lastNow = now;
            }
            // A disconnected actor may resolve its existing held yield. This is
            // not permission to dispatch a new native call or restore membership.
            failure = SourceEntryFacts(member, facts, false);
            if (failure != CargoReason.None) return Result(failure, capture.Id);
            if (member.Revision == long.MaxValue) return Result(CargoReason.QuotaExceeded, capture.Id);
            double weight = CargoValues.ProductWeight(capture.SelectedProducts);
            failure = Capacity(member, weight, facts.CapacityPolicy);
            if (failure != CargoReason.None) return Result(failure, capture.Id);
            var request = new CargoCaptureRequest
            {
                ExpeditionId = capture.Intent.ExpeditionId, MemberId = capture.Intent.MemberId, RequestId = capture.Intent.RequestId,
                OperationId = lease.OperationId, BagRevision = capture.Intent.BagRevision, Source = CargoValues.Copy(capture.Intent.Source),
                Products = CargoValues.CopyProducts(capture.SelectedProducts)
            };
            capture.Request = request; capture.ProductsFingerprint = productsFingerprint; capture.Weight = weight;
            member.Reserved += weight; member.Revision++; RememberNativeWeightSample(member, facts); _lastNow = now;
            // FreezeReturn sealed capture membership, not fabricated products.
            // A late yield can fill only this pre-existing capture's batch items.
            if (_returnId != null) AddReturnItems(capture);
            return Result(CargoReason.None, capture.Id);
        }

        public CargoResult CancelSelectionNotEntered(CargoSourceLease lease, CargoSourceFacts facts, double now)
        {
            if (!TrySourceLease(lease, out Capture capture, out CargoReason failure)) return Result(failure);
            if (capture.Stage != CargoCaptureStage.Reserved || capture.Request != null) return Result(CargoReason.InvalidStage, capture.Id);
            Member member = _members[capture.Intent.MemberId];
            failure = BoundSourceFacts(member, capture, facts, now);
            if (failure != CargoReason.None) return Result(failure, capture.Id);
            if (!facts.NativeNotEntered) return Result(CargoReason.NotEnteredProofRequired, capture.Id);
            if (member.Revision == long.MaxValue) return Result(CargoReason.QuotaExceeded, capture.Id);
            capture.Stage = CargoCaptureStage.NativeNotEntered; _sourceLeases.Remove(capture.SourceKey); member.Revision++; _lastNow = now;
            return Result(CargoReason.None, capture.Id);
        }

        public CargoResult EnterCapture(long captureId, CargoCaptureFacts freshFacts, double now)
        {
            if (!TryCapture(captureId, out Capture capture, out CargoReason failure)) return Result(failure);
            if (capture.Request == null || capture.SourceLease != null) return Result(CargoReason.InvalidStage, captureId);
            if (_phase != CargoExpeditionPhase.Active) return Result(CargoReason.ReturnFrozen, captureId);
            if (capture.Stage != CargoCaptureStage.Reserved) return Result(CargoReason.InvalidStage, captureId);
            Member member = _members[capture.Request.MemberId];
            failure = CaptureFacts(member, capture.Request, capture.ProductsFingerprint, freshFacts, now);
            if (failure == CargoReason.None) failure = EntryFacts(member, freshFacts);
            if (failure == CargoReason.None && !freshFacts.NativeEntryCapabilityVerified) failure = CargoReason.MissingCapability;
            if (failure == CargoReason.None) failure = Capacity(member, 0, freshFacts.CapacityPolicy);
            if (failure != CargoReason.None) return Result(failure, captureId);
            // This one-way CLR fence precedes a future adapter's native entry.
            // Unknown remains reserved forever; there is no timeout or retry.
            capture.Stage = CargoCaptureStage.EnteredUnknown; _lastNow = now;
            RememberNativeWeightSample(member, freshFacts);
            return Result(CargoReason.None, captureId);
        }

        public CargoResult CaptureNotEntered(long captureId, CargoCaptureFacts facts, double now)
        {
            if (!TryCapture(captureId, out Capture capture, out CargoReason failure)) return Result(failure);
            if (capture.Request == null || capture.SourceLease != null) return Result(CargoReason.InvalidStage, captureId);
            if (capture.Stage != CargoCaptureStage.Reserved) return Result(CargoReason.InvalidStage, captureId);
            Member member = _members[capture.Request.MemberId];
            failure = CaptureFacts(member, capture.Request, capture.ProductsFingerprint, facts, now);
            if (failure != CargoReason.None) return Result(failure, captureId);
            if (!facts.NativeNotEntered) return Result(CargoReason.NotEnteredProofRequired, captureId);
            if (member.Revision == long.MaxValue) return Result(CargoReason.QuotaExceeded, captureId);
            capture.Stage = CargoCaptureStage.NativeNotEntered;
            member.Reserved = Math.Max(0, member.Reserved - capture.Weight); member.Revision++;
            _sourceLeases.Remove(capture.SourceKey);
            for (int i = 0; i < capture.Request.Products.Length; i++)
                if (_returnItems.TryGetValue((captureId, i), out ReturnItem item)) item.Stage = CargoReturnStage.NotRequired;
            _lastNow = now; return Result(CargoReason.None, captureId);
        }

        public CargoResult ObserveHostBagWeight(string memberId, CargoCaptureFacts facts, double now)
        {
            if (!TryMember(memberId, out Member member, out CargoReason failure)) return Result(failure);
            if (member.Setup.BagMode != CargoBagMode.HostNative) return Result(CargoReason.WrongBagMode);
            failure = MemberFacts(member, facts, now);
            if (failure != CargoReason.None) return Result(failure);
            if (facts.BagRevision < 1) return Result(CargoReason.InvalidInput);
            if (facts.BagRevision != member.Revision) return Result(CargoReason.Conflict);
            if (!facts.HostBagWeightVerified) return Result(CargoReason.MissingCapability);
            try { CargoValues.Weight(facts.NativeCurrentWeight); } catch (ArgumentException) { return Result(CargoReason.InvalidInput); }
            if (facts.SampledAt < member.NativeWeightSampledAt) return Result(CargoReason.StaleFacts);
            if (member.Revision == long.MaxValue) return Result(CargoReason.QuotaExceeded);
            if (member.Weight == facts.NativeCurrentWeight)
            {
                // Unchanged totals still advance revision: two observations
                // in one Unity frame can have the same timestamp, and the
                // older facts must not remain usable after the newer sample.
                member.NativeWeightSampledAt = facts.SampledAt; member.Revision++; _lastNow = now;
                return Result(CargoReason.Duplicate);
            }
            member.Weight = facts.NativeCurrentWeight; member.NativeWeightSampledAt = facts.SampledAt; member.Revision++; _lastNow = now;
            return Result(CargoReason.None);
        }

        public CargoResult ConfirmCapture(long captureId, CargoReceiptKind kind, CargoProduct[] actualProducts, CargoCaptureFacts facts, double now)
        {
            CargoProduct[] owned; string productFingerprint;
            try { owned = CargoValues.CopyProducts(actualProducts); productFingerprint = CargoValues.ProductsFingerprint(owned); }
            catch (ArgumentException) { return Result(CargoReason.InvalidInput); }
            if (!Enum.IsDefined(typeof(CargoReceiptKind), kind)) return Result(CargoReason.InvalidInput);
            if (!TryCapture(captureId, out Capture capture, out CargoReason failure)) return Result(failure);
            if (capture.Request == null) return Result(CargoReason.InvalidStage, captureId);
            Member member = _members[capture.Request.MemberId];
            failure = CaptureFacts(member, capture.Request, capture.ProductsFingerprint, facts, now);
            if (failure != CargoReason.None) return Result(failure, captureId);
            if (productFingerprint != capture.ProductsFingerprint) return Result(CargoReason.Conflict, captureId);
            CargoReceiptKind expected = member.Setup.BagMode == CargoBagMode.HostNative ? CargoReceiptKind.HostNativeBagDelta : CargoReceiptKind.EmployeeDivertedYield;
            if (kind != expected) return Result(CargoReason.WrongBagMode, captureId);
            if (capture.Stage == CargoCaptureStage.Confirmed) return Result(CargoReason.Duplicate, captureId);
            if (capture.Stage != CargoCaptureStage.EnteredUnknown) return Result(CargoReason.InvalidStage, captureId);
            if (!facts.YieldVerified || !facts.CaptureTerminal) return Result(CargoReason.MissingCapability, captureId);
            double confirmedWeight;
            if (kind == CargoReceiptKind.HostNativeBagDelta)
            {
                if (!facts.ActualBagDeltaVerified || !facts.HostBagWeightVerified) return Result(CargoReason.MissingCapability, captureId);
                // Historical delta provenance may arrive late, but its current
                // native total must be rebound to the current bag revision.
                if (facts.BagRevision != member.Revision) return Result(CargoReason.Conflict, captureId);
                if (facts.SampledAt < member.NativeWeightSampledAt) return Result(CargoReason.StaleFacts, captureId);
                try
                {
                    CargoValues.Weight(facts.NativeBagWeightBefore); CargoValues.Weight(facts.NativeBagWeightAfter); CargoValues.Weight(facts.NativeCurrentWeight);
                }
                catch (ArgumentException) { return Result(CargoReason.InvalidInput, captureId); }
                double delta = facts.NativeBagWeightAfter - facts.NativeBagWeightBefore;
                if (Math.Abs(delta - capture.Weight) > 1e-9 * Math.Max(1, capture.Weight)) return Result(CargoReason.Conflict, captureId);
                // The native total can already include this capture. Never add
                // the mirrored products' weight again to the host native bag.
                confirmedWeight = facts.NativeCurrentWeight;
            }
            else
            {
                if (!facts.DiversionVerified || !facts.NoHostBagWrite || !facts.CapacityRoutingVerified || !facts.CapacityPolicyVerified)
                    return Result(CargoReason.MissingCapability, captureId);
                failure = Capacity(member, 0, facts.CapacityPolicy);
                if (failure != CargoReason.None) return Result(failure, captureId);
                confirmedWeight = member.Weight + capture.Weight;
            }
            if (member.Revision == long.MaxValue) return Result(CargoReason.QuotaExceeded, captureId);
            capture.Stage = CargoCaptureStage.Confirmed; capture.ReceiptKind = kind;
            member.Weight = confirmedWeight; member.Reserved = Math.Max(0, member.Reserved - capture.Weight); member.Revision++;
            RememberNativeWeightSample(member, facts);
            _lastNow = now; return Result(CargoReason.None, captureId);
        }

        public CargoResult FreezeReturn(string returnId)
        {
            string owned;
            try { owned = CargoValues.GuidKey(returnId); } catch (ArgumentException) { return Result(CargoReason.InvalidInput); }
            if (_returnId != null) return Result(_returnId == owned ? CargoReason.Duplicate : CargoReason.Conflict);
            if (_phase != CargoExpeditionPhase.Active) return Result(CargoReason.InvalidStage);
            // Membership and every product index are sealed, including pending
            // captures. Late proven receipts can resolve those original members
            // of the batch, but cannot append a new reserve/capture.
            foreach (Capture capture in _captures.Values) if (capture.Request != null) AddReturnItems(capture);
            _returnId = owned; _phase = CargoExpeditionPhase.Returning;
            return Result(CargoReason.None);
        }

        public CargoResult LeaseMaterialization(long captureId, int productIndex, CargoStorageFacts facts, double now)
        {
            if (!TryReturnItem(captureId, productIndex, out ReturnItem item, out CargoReason failure)) return Result(failure, captureId);
            if (_members[item.Capture.Request.MemberId].Setup.BagMode != CargoBagMode.EmployeeVirtual) return Result(CargoReason.WrongBagMode, captureId);
            if (_phase != CargoExpeditionPhase.Returning || item.Capture.Stage != CargoCaptureStage.Confirmed || item.Stage != CargoReturnStage.Unclaimed)
                return Result(CargoReason.InvalidStage, captureId);
            failure = StorageFacts(item, facts, now);
            if (failure != CargoReason.None) return Result(failure, captureId);
            if (!facts.EmployeeStorageAdapterVerified) return Result(CargoReason.MissingCapability, captureId);
            item.Stage = CargoReturnStage.Leased; _lastNow = now;
            return Result(CargoReason.None, captureId);
        }

        public CargoResult EnterMaterialization(long captureId, int productIndex, CargoStorageFacts freshFacts, double now)
        {
            if (!TryReturnItem(captureId, productIndex, out ReturnItem item, out CargoReason failure)) return Result(failure, captureId);
            if (_members[item.Capture.Request.MemberId].Setup.BagMode != CargoBagMode.EmployeeVirtual) return Result(CargoReason.WrongBagMode, captureId);
            if (_phase != CargoExpeditionPhase.Returning || item.Stage != CargoReturnStage.Leased) return Result(CargoReason.InvalidStage, captureId);
            failure = StorageFacts(item, freshFacts, now);
            if (failure != CargoReason.None) return Result(failure, captureId);
            if (!freshFacts.EmployeeStorageAdapterVerified || !freshFacts.NativeEntryCapabilityVerified) return Result(CargoReason.MissingCapability, captureId);
            item.Stage = CargoReturnStage.EnteredUnknown; _lastNow = now;
            return Result(CargoReason.None, captureId);
        }

        public CargoResult ObserveEmployeeStorage(long captureId, int productIndex, CargoStorageFacts facts, double now)
            => ObserveStorage(captureId, productIndex, facts, now, CargoBagMode.EmployeeVirtual);

        public CargoResult ObserveHostStorage(long captureId, int productIndex, CargoStorageFacts facts, double now)
            => ObserveStorage(captureId, productIndex, facts, now, CargoBagMode.HostNative);

        private CargoResult ObserveStorage(long captureId, int productIndex, CargoStorageFacts facts, double now, CargoBagMode mode)
        {
            if (!TryReturnItem(captureId, productIndex, out ReturnItem item, out CargoReason failure)) return Result(failure, captureId);
            if (_members[item.Capture.Request.MemberId].Setup.BagMode != mode) return Result(CargoReason.WrongBagMode, captureId);
            bool expectedStage = mode == CargoBagMode.HostNative ? item.Stage == CargoReturnStage.Unclaimed : item.Stage == CargoReturnStage.EnteredUnknown;
            if ((_phase != CargoExpeditionPhase.Returning && _phase != CargoExpeditionPhase.Aborted) ||
                item.Capture.Stage != CargoCaptureStage.Confirmed || !expectedStage) return Result(CargoReason.InvalidStage, captureId);
            failure = StorageFacts(item, facts, now);
            if (failure != CargoReason.None) return Result(failure, captureId);
            if (!facts.StorageDeltaVerified || (mode == CargoBagMode.HostNative ? !facts.HostNativeStorageChainVerified : !facts.EmployeeStorageAdapterVerified))
                return Result(CargoReason.MissingCapability, captureId);
            // Host mode only records its original storage chain. It has no
            // employee-style materialization lease or dispatch operation.
            item.Stage = CargoReturnStage.StorageObserved; _lastNow = now;
            return Result(CargoReason.None, captureId);
        }

        public CargoResult ConfirmStorageSave(long captureId, int productIndex, CargoStorageFacts facts, double now)
        {
            if (!TryReturnItem(captureId, productIndex, out ReturnItem item, out CargoReason failure)) return Result(failure, captureId);
            if ((_phase != CargoExpeditionPhase.Returning && _phase != CargoExpeditionPhase.Aborted) || item.Stage != CargoReturnStage.StorageObserved)
                return Result(CargoReason.InvalidStage, captureId);
            failure = StorageFacts(item, facts, now);
            if (failure != CargoReason.None) return Result(failure, captureId);
            if (!facts.SaveConfirmed) return Result(CargoReason.MissingCapability, captureId);
            item.Stage = CargoReturnStage.SaveConfirmed; _lastNow = now;
            return Result(CargoReason.None, captureId);
        }

        public CargoResult CompleteReturn()
        {
            if (_phase != CargoExpeditionPhase.Returning) return Result(CargoReason.InvalidStage);
            if (_captures.Values.Any(capture => capture.Stage == CargoCaptureStage.Reserved || capture.Stage == CargoCaptureStage.EnteredUnknown) ||
                _returnItems.Values.Any(item => item.Stage != CargoReturnStage.SaveConfirmed && item.Stage != CargoReturnStage.NotRequired))
                return Result(CargoReason.ReturnIncomplete);
            _phase = CargoExpeditionPhase.Returned;
            return Result(CargoReason.None);
        }

        public CargoResult Abort()
        {
            if (_phase == CargoExpeditionPhase.Returned) return Result(CargoReason.InvalidStage);
            if (_phase == CargoExpeditionPhase.Aborted) return Result(CargoReason.Duplicate);
            _phase = CargoExpeditionPhase.Aborted;
            // Never manufacture normal return, cancellation or no-entry
            // evidence. Keep partial success and every unknown reservation.
            return Result(CargoReason.None);
        }

        private CargoMemberSnapshot MemberSnapshot(Member member)
        {
            var inventory = new List<CargoInventoryItem>();
            if (_phase != CargoExpeditionPhase.Returned)
                foreach (Capture capture in _captures.Values.Where(item => CaptureMemberId(item) == member.Setup.MemberId && item.Stage == CargoCaptureStage.Confirmed).OrderBy(item => item.Id))
                    for (int i = 0; i < capture.Request.Products.Length; i++) inventory.Add(new CargoInventoryItem
                    { CaptureId = capture.Id, ProductIndex = i, Product = CargoValues.Copy(capture.Request.Products[i]) });
            return new CargoMemberSnapshot
            {
                MemberId = member.Setup.MemberId, BagMode = member.Setup.BagMode, Capacity = member.Setup.Capacity,
                Weight = member.Weight, ReservedWeight = member.Reserved, BagRevision = member.Revision,
                Connected = member.Connected, HighestRequestId = member.HighestRequest, Inventory = inventory.ToArray()
            };
        }

        private CargoReason EntryFacts(Member member, CargoCaptureFacts facts)
        {
            if (!member.Connected) return CargoReason.Disconnected;
            CargoReason room = RoomFacts(facts);
            if (room != CargoReason.None) return room;
            if (facts.BagRevision != member.Revision) return CargoReason.Conflict;
            if (member.Setup.BagMode == CargoBagMode.HostNative && (!facts.HostBagWeightVerified || facts.NativeCurrentWeight != member.Weight))
                return facts.HostBagWeightVerified ? CargoReason.Conflict : CargoReason.MissingCapability;
            if (member.Setup.BagMode == CargoBagMode.HostNative && facts.SampledAt < member.NativeWeightSampledAt)
                return CargoReason.StaleFacts;
            if (!facts.ActorPermitted || !facts.SourceAvailable || !facts.CapacityPolicyVerified ||
                (member.Setup.BagMode == CargoBagMode.EmployeeVirtual && !facts.CapacityRoutingVerified)) return CargoReason.MissingCapability;
            return CargoReason.None;
        }
        private static void RememberNativeWeightSample(Member member, CargoCaptureFacts facts)
        {
            if (member.Setup.BagMode == CargoBagMode.HostNative) member.NativeWeightSampledAt = facts.SampledAt;
        }
        private static void RememberNativeWeightSample(Member member, CargoSourceFacts facts)
        {
            if (member.Setup.BagMode == CargoBagMode.HostNative) member.NativeWeightSampledAt = facts.SampledAt;
        }
        private CargoReason SourceFacts(Member member, CargoSourceIntent intent, CargoSourceFacts facts, double now)
        {
            if (facts == null) return CargoReason.MissingCapability;
            CargoReason failure = Fresh(facts.SampledAt, now);
            if (failure != CargoReason.None) return failure;
            try
            {
                if (intent.ExpeditionId != _expedition || CargoValues.GuidKey(facts.ExpeditionId) != _expedition ||
                    CargoValues.GuidKey(facts.MemberId) != member.Setup.MemberId || facts.BoundPlayerId != member.PlayerId ||
                    facts.RequestId != intent.RequestId || facts.ActorRevision != intent.ActorRevision || facts.LoadoutRevision != intent.LoadoutRevision ||
                    CargoValues.SourceKey(facts.Source) != CargoValues.SourceKey(intent.Source)) return CargoReason.WrongIdentity;
                string currentRoom = CargoValues.GuidKey(facts.CurrentRoomId);
                if (currentRoom != intent.Source.RoomId || (_sourceRoom != null && currentRoom != _sourceRoom)) return CargoReason.WrongIdentity;
                if (facts.BagRevision < 1) return CargoReason.InvalidInput;
                CargoValues.Weight(facts.NativeCurrentWeight);
            }
            catch (ArgumentException) { return CargoReason.InvalidInput; }
            if (!facts.HostAuthority || !facts.SourceIdentityVerified || !facts.OrdinaryFishVerified) return CargoReason.MissingCapability;
            if (facts.CapacityPolicyVerified && !Enum.IsDefined(typeof(CargoCapacityPolicy), facts.CapacityPolicy)) return CargoReason.InvalidInput;
            return CargoReason.None;
        }
        private CargoReason BoundSourceFacts(Member member, Capture capture, CargoSourceFacts facts, double now)
        {
            CargoReason failure = SourceFacts(member, capture.Intent, facts, now);
            if (failure != CargoReason.None) return failure;
            return facts.OperationId == capture.SourceLease.OperationId && facts.IntentFingerprint == capture.Fingerprint
                ? CargoReason.None : CargoReason.WrongIdentity;
        }
        private CargoReason SourceEntryFacts(Member member, CargoSourceFacts facts, bool newEntry)
        {
            if (newEntry && !member.Connected) return CargoReason.Disconnected;
            if (facts.BagRevision != member.Revision) return CargoReason.Conflict;
            if (!facts.CapacityPolicyVerified || (member.Setup.BagMode == CargoBagMode.EmployeeVirtual && !facts.CapacityRoutingVerified))
                return CargoReason.MissingCapability;
            if (newEntry && (!facts.ActorPermitted || !facts.SourceAvailable)) return CargoReason.MissingCapability;
            if (member.Setup.BagMode == CargoBagMode.HostNative)
            {
                if (!facts.HostBagWeightVerified) return CargoReason.MissingCapability;
                if (facts.NativeCurrentWeight != member.Weight) return CargoReason.Conflict;
                if (facts.SampledAt < member.NativeWeightSampledAt) return CargoReason.StaleFacts;
            }
            return CargoReason.None;
        }
        private bool TrySourceLease(CargoSourceLease lease, out Capture capture, out CargoReason failure)
        {
            capture = null; failure = lease == null ? CargoReason.InvalidInput : CargoReason.WrongIdentity;
            if (lease == null || !_captures.TryGetValue(lease.CaptureId, out Capture candidate) || !ReferenceEquals(candidate.SourceLease, lease)) return false;
            capture = candidate; failure = CargoReason.None; return true;
        }
        private void AddReturnItems(Capture capture)
        {
            for (int i = 0; i < capture.Request.Products.Length; i++)
                _returnItems.Add((capture.Id, i), new ReturnItem
                {
                    Capture = capture, Index = i,
                    Stage = capture.Stage == CargoCaptureStage.NativeNotEntered ? CargoReturnStage.NotRequired : CargoReturnStage.Unclaimed
                });
        }
        private static string CaptureMemberId(Capture capture) => capture.Intent?.MemberId ?? capture.Request.MemberId;
        private static CargoSource CaptureSource(Capture capture) => capture.Intent?.Source ?? capture.Request.Source;
        private static long CaptureOperation(Capture capture) => capture.SourceLease?.OperationId ?? capture.Request.OperationId;
        private CargoReason CaptureFacts(Member member, CargoCaptureRequest request, string productFingerprint, CargoCaptureFacts facts, double now)
        {
            CargoReason failure = MemberFacts(member, facts, now);
            if (failure != CargoReason.None) return failure;
            try
            {
                if (facts.BagRevision < 1) return CargoReason.InvalidInput;
                CargoValues.Weight(facts.NativeBagWeightBefore); CargoValues.Weight(facts.NativeBagWeightAfter); CargoValues.Weight(facts.NativeCurrentWeight);
                if (request.ExpeditionId != _expedition || facts.RequestId != request.RequestId || facts.OperationId != request.OperationId ||
                    CargoValues.SourceKey(facts.Source) != CargoValues.SourceKey(request.Source)) return CargoReason.WrongIdentity;
            }
            catch (ArgumentException) { return CargoReason.InvalidInput; }
            if (!facts.SourceIdentityVerified || !facts.OrdinaryFishVerified || !facts.YieldVerified || facts.ProductsFingerprint != productFingerprint)
                return CargoReason.MissingCapability;
            if (facts.CapacityPolicyVerified && !Enum.IsDefined(typeof(CargoCapacityPolicy), facts.CapacityPolicy)) return CargoReason.InvalidInput;
            return CargoReason.None;
        }
        private CargoReason RoomFacts(CargoCaptureFacts facts)
        {
            try
            {
                string currentRoom = CargoValues.GuidKey(facts.CurrentRoomId);
                if (currentRoom != CargoValues.GuidKey(facts.Source.RoomId) || (_sourceRoom != null && currentRoom != _sourceRoom)) return CargoReason.WrongIdentity;
            }
            catch (ArgumentException) { return CargoReason.InvalidInput; }
            return CargoReason.None;
        }
        private CargoReason MemberFacts(Member member, CargoCaptureFacts facts, double now)
        {
            if (facts == null) return CargoReason.MissingCapability;
            CargoReason failure = Fresh(facts.SampledAt, now);
            if (failure != CargoReason.None) return failure;
            try
            {
                if (CargoValues.GuidKey(facts.ExpeditionId) != _expedition || CargoValues.GuidKey(facts.MemberId) != member.Setup.MemberId ||
                    facts.BoundPlayerId != member.PlayerId) return CargoReason.WrongIdentity;
            }
            catch (ArgumentException) { return CargoReason.InvalidInput; }
            return facts.HostAuthority ? CargoReason.None : CargoReason.MissingCapability;
        }
        private CargoReason StorageFacts(ReturnItem item, CargoStorageFacts facts, double now)
        {
            if (facts == null) return CargoReason.MissingCapability;
            CargoReason failure = Fresh(facts.SampledAt, now);
            if (failure != CargoReason.None) return failure;
            try
            {
                if (CargoValues.GuidKey(facts.ExpeditionId) != _expedition || CargoValues.GuidKey(facts.ReturnId) != _returnId ||
                    CargoValues.GuidKey(facts.MemberId) != item.Capture.Request.MemberId || facts.CaptureId != item.Capture.Id || facts.ProductIndex != item.Index ||
                    facts.ProductFingerprint != CargoValues.ProductFingerprint(item.Capture.Request.Products[item.Index])) return CargoReason.WrongIdentity;
            }
            catch (ArgumentException) { return CargoReason.InvalidInput; }
            return facts.HostAuthority ? CargoReason.None : CargoReason.MissingCapability;
        }
        private CargoReason Fresh(double sampledAt, double now)
        {
            if (!double.IsFinite(now) || now < 0 || now < _lastNow) return CargoReason.InvalidInput;
            if (!double.IsFinite(sampledAt) || sampledAt < 0 || sampledAt > now || now - sampledAt > CargoValues.MaxFactsAge) return CargoReason.StaleFacts;
            return CargoReason.None;
        }
        private static CargoReason Capacity(Member member, double additional, CargoCapacityPolicy policy)
        {
            double proposed = member.Weight + member.Reserved + additional;
            if (!double.IsFinite(proposed) || proposed > CargoValues.MaxWeight) return CargoReason.QuotaExceeded;
            return policy == CargoCapacityPolicy.RejectOverCapacity && proposed > member.Setup.Capacity ? CargoReason.CapacityExceeded : CargoReason.None;
        }
        private bool TryMember(string memberId, out Member member, out CargoReason failure)
        {
            member = null;
            try
            {
                failure = _members.TryGetValue(CargoValues.GuidKey(memberId), out member) ? CargoReason.None : CargoReason.UnknownMember;
                return member != null;
            }
            catch (ArgumentException) { failure = CargoReason.InvalidInput; return false; }
        }
        private bool TryCapture(long captureId, out Capture capture, out CargoReason failure)
        {
            failure = captureId < 1 ? CargoReason.InvalidInput : CargoReason.NotFound;
            if (!_captures.TryGetValue(captureId, out capture)) return false;
            failure = CargoReason.None; return true;
        }
        private bool TryReturnItem(long captureId, int productIndex, out ReturnItem item, out CargoReason failure)
        {
            failure = captureId < 1 || productIndex < 0 || productIndex >= CargoValues.MaxProductsPerCapture ? CargoReason.InvalidInput : CargoReason.NotFound;
            if (!_returnItems.TryGetValue((captureId, productIndex), out item)) return false;
            failure = CargoReason.None; return true;
        }
        private static CargoResult Result(CargoReason reason, long captureId = 0) => new CargoResult(reason, captureId);
    }
}
