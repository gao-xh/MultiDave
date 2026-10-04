using System;
using System.Numerics;
using DaveCoop.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("cargo return bound materializer enters ledger and arbitrates once", CargoReturnMaterializerTests.BoundPlanDispatchEntersLedgerAndArbitratesOnce),
            ("cargo return materializer guard and add failures stay unknown", CargoReturnMaterializerTests.GuardAndAddFailuresRemainUnknownWithoutRetry),
            ("cargo return materializer creator thread and reentry cannot redispatch", CargoReturnMaterializerTests.CreatorThreadAndReentrantCallsCannotDispatchAgain),
            ("cargo return materializer fresh facts and exact plan identity", CargoReturnMaterializerTests.FreshFactsAndExactPlanIdentityRejectBeforeBackend),
            ("cargo return capture and conversion grades stay independent and owned", CargoReturnPlanTests.CaptureAndReturnGradesRemainIndependentAndOwned),
            ("cargo return binding rejects identity and unverified conversion", CargoReturnPlanTests.BindingRejectsInvalidIdentityAndUnverifiedConversion),
            ("cargo return first policy and output stay fixed after native entry", CargoReturnPlanTests.FirstPolicyAndOutputStayFixedThroughUnknownMaterialization),
            ("cargo return employee stages require plan host original chain does not", CargoReturnPlanTests.EveryEmployeeStageRequiresItsPlanWhileHostKeepsTheOriginalChain),
            ("cargo return partial save disconnect and abort retain unknown plan", CargoReturnPlanTests.PartialSaveDisconnectAndAbortKeepTheUnknownPlan),
            ("cargo return late confirmed capture binds only original products", CargoReturnPlanTests.FrozenLateConfirmedCaptureBindsOnlyItsOriginalProducts),
            ("fish product original TID capture grade and float weights", FishYieldProductTests.OriginalTidCaptureGradeAndFloatWeightsStayFixed),
            ("fish product lift policy raw values and owned snapshots", FishYieldProductTests.LiftPolicyAndSnapshotsPreserveRawValuesAndOwnedProducts),
            ("fish product getter failures retain partial results without retry", FishYieldProductTests.GetterFailuresRetainEveryReturnedScalarWithoutRetry),
            ("fish product source loss preserves returned getter prefix", FishYieldProductTests.SourceLossStopsGettersAndPreservesTheReturnedPrefix),
            ("fish product invalid scalars and grade overflow remain unknown", FishYieldProductTests.InvalidScalarsAndGradeOverflowRemainUnknown),
            ("fish product empty sentinels cannot prove empty capture", FishYieldProductTests.EmptyOriginalResultsCannotBecomeAnEmptyCaptureReceipt),
            ("fish product creator thread and reentry cannot duplicate normalization", FishYieldProductTests.CreatorThreadAndReentryCannotDuplicateNormalization),
            ("fish product absent backend and preselection do not consume attempt", FishYieldProductTests.MissingBackendAndPreselectionCallsDoNotConsumeAnAttempt),
            ("fish product capacity retry uses cached plan without getters", FishYieldProductTests.CapacityRetryUsesOneCachedPlanAndNeverReadsResourcesAgain),
            ("fish product fresh late facts seal without capture receipt", FishYieldProductTests.FreshLateFactsSealProductsWithoutGrantingAReceipt),
            ("fish yield grade main and plus are ordered once after ledger entry", FishYieldSelectionTests.GradeMainAndPlusAreOrderedOnceAfterLedgerEntry),
            ("fish yield competing coordinators cannot reselect one lease", FishYieldSelectionTests.CompetingCoordinatorsOnOneLeaseCannotRunAnotherSelection),
            ("fish yield partial business failures retain unknown results", FishYieldSelectionTests.PartialBusinessFailuresKeepUnknownResultsAndNeverRetry),
            ("fish yield source guard failures stop later business", FishYieldSelectionTests.SourceGuardFailuresStopLaterCallsWithoutErasingEarlierResults),
            ("fish yield missing stale and foreign facts dispatch nothing", FishYieldSelectionTests.MissingStaleAndForeignEntryFactsDispatchNothingUntilFresh),
            ("fish yield dead body recipe uses one tier without pickup grade RNG", FishYieldSelectionTests.DeadBodyRecipeUsesOneTierAndDoesNotSelectPickupGrade),
            ("fish yield tier bounds preserve first tier and plus budget", FishYieldSelectionTests.TierBoundsPreserveAtLeastOneMainAndPreflightThePlusSlot),
            ("fish yield sentinel and unexpected results never lookup or reroll", FishYieldSelectionTests.ExplicitNoDropAndUnexpectedResultsNeverLookupOrReroll),
            ("fish yield reentry and wrong thread dispatch no new business", FishYieldSelectionTests.ReentrantAndWrongThreadAttemptsCannotDispatchMoreBusiness),
            ("fish yield capacity rejection keeps raw batch and duplicate entries", FishYieldSelectionTests.CapacityRejectionKeepsTheRawBatchAndDuplicateResourcesStayDistinct),
            ("loot slot scalar signed boundaries and immutable inputs", LootSlotSnapshotTests.SignedBoundaryVectorsRemainBitExact),
            ("loot slot scalar inactive fake value does not reject or repair", LootSlotSnapshotTests.InactiveFakeValueDoesNotRejectOrRepairTheSnapshot),
            ("loot slot scalar initialization and zero key remain unavailable", LootSlotSnapshotTests.MissingInitializationAndZeroKeyNeverInventAValue),
            ("loot slot scalar tamper and later grades cannot grant cargo proof", LootSlotSnapshotTests.TamperAndLaterGradeCandidatesCannotGrantCargoProof),
            ("cargo selection capacity rejection cannot replace the selected batch", CargoLateYieldTests.CapacityDeniedSelectedYieldCannotBeReplacedOrRerolled),
            ("cargo selection source leases mint global IDs without inventing yield", CargoLateYieldTests.SourceOnlyLeasesMintAcrossMembersWithoutInventingYield),
            ("cargo selection requires isolation before any native business", CargoLateYieldTests.SelectionEntryNeedsAnIsolationBarrierBeforeAnyNativeBusiness),
            ("cargo selection late yield reserves personal weight and requires receipt", CargoLateYieldTests.CompleteLateYieldReservesPersonalWeightButNeedsAReceipt),
            ("cargo selection missing coverage and capacity preserve unknown source", CargoLateYieldTests.MissingYieldCoverageAndOverCapacityKeepTheEnteredSourceUnknown),
            ("cargo selection replay cancellation and permanent tombstones", CargoLateYieldTests.SourceReplayAndNotEnteredCancellationKeepAllTombstones),
            ("cargo selection unknown survives epoch room disconnect return and abort", CargoLateYieldTests.UnselectedUnknownSurvivesEpochRoomDisconnectReturnAndAbort),
            ("cargo selection frozen offline capture seals only its original batch", CargoLateYieldTests.FrozenDisconnectedCaptureCanSealAndSettleOnlyItsOriginalBatch),
            ("cargo selection host fresh baseline and native total counted once", CargoLateYieldTests.HostLateYieldRequiresFreshBaselineAndUsesNativeTotalOnce),
            ("cargo selection opaque leases and owned snapshot copies", CargoLateYieldTests.OpaqueLeaseAndSnapshotCopiesCannotChangeSourceOrOwnership),
            ("cargo selection legacy and source captures share global quota", CargoLateYieldTests.LegacyReservationsAndSourceLeasesSharePermanentGlobalQuota),
            ("TCP cargo selection unknown survives scene disconnect and return barrier", () => CargoLateYieldTransportTests.UnselectedUnknownSurvivesSceneDisconnectAndReturnBarrier().GetAwaiter().GetResult()),
            ("TCP cargo selection late yield preserves employee ownership and return batch", () => CargoLateYieldTransportTests.LateSelectedYieldPublishesOnlyEmployeeBagAndOriginalReturnBatch().GetAwaiter().GetResult()),
            ("loot lineage same fish drop plus bag and original roll enclosure", LootCallLineageTests.SameFishDropPlusBagAndRollKeepFixedEnclosure),
            ("loot lineage different fish and unknown scopes mask parent", LootCallLineageTests.DifferentFishAndUnknownScopesMaskTheOuterCandidate),
            ("loot lineage fixed prefix survives pool generation change", LootCallLineageTests.FrozenSourceAndPoolGenerationNeverRebindAfterPrefix),
            ("loot lineage postfix and original exceptions are not capture terminal", LootCallLineageTests.PostfixAndOriginalExceptionNeverMeanCaptureTerminal),
            ("loot lineage missing postfix and changed scalar result lose integrity", LootCallLineageTests.MissingPostfixAndChangedScalarResultsLoseIntegrity),
            ("loot lineage wrong thread cannot supply or close a scope", LootCallLineageTests.WrongThreadCannotSupplyOrCloseAParentScope),
            ("loot lineage high water foreign run and LIFO reject rebinding", LootCallLineageTests.HighWaterForeignRunAndLifoChecksRejectRebinding),
            ("loot lineage queue depth and run quota loss cannot heal", LootCallLineageTests.QueueDepthAndRunQuotaLossNeverHealAfterDrain),
            ("loot lineage stop counts pending and queued without restart", LootCallLineageTests.StopCountsPendingAndQueuedEvidenceWithoutRestart),
            ("cargo transport ledger pending and historical weight projection", CargoTransportTests.LedgerProjectionKeepsPendingAndHistoricalWeights),
            ("cargo transport returned ledger floating reservation residue", CargoTransportTests.ReturnedLedgerRetainsFloatingReservationResidue),
            ("cargo transport canonical fingerprints and deep copies", CargoTransportTests.CanonicalFingerprintAndDeepCopiesOwnTheirData),
            ("cargo transport atomic pages retraction and output ownership", CargoTransportTests.AtomicPagesRetractionAndOutputOwnership),
            ("cargo transport replay clear and expedition identity fences", CargoTransportTests.ReplayClearAndExpeditionFencesDoNotReset),
            ("cargo transport malformed personal members and products", CargoTransportTests.InvalidSchemaRejectsMixedMembersAndProducts),
            ("cargo transport exact page bounds and corrupt assemblies", CargoTransportTests.ExactSliceBoundsAndCorruptAssembliesFailClosed),
            ("cargo transport member phase and room revision continuity", CargoTransportTests.StableMemberPhaseAndRoomRevisionContinuity),
            ("cargo session codec role and room binding", CargoSessionTests.CargoCodecRolesAndRoomIdentity),
            ("cargo session atomic owned snapshots and mailbox retraction", CargoSessionTests.CargoAtomicOwnedSnapshotsAndMailboxRetraction),
            ("cargo session slow consumer completes started batch and coalesces latest", CargoSessionTests.CargoSlowConsumerFinishesStartedBatchAndCoalescesLatest),
            ("cargo session revision expedition and lifecycle fences", CargoSessionTests.CargoRevisionExpeditionAndLifecycleFences),
            ("cargo session scene retention and close preserves ledger", CargoSessionTests.CargoSceneRetentionAndCloseDoesNotTouchLedger),
            ("cargo session control priority and five lane fairness", CargoSessionTests.CargoControlPriorityAndFiveLaneFairness),
            ("TCP cargo complete snapshot round trip", () => CargoSessionTests.CargoTcpCompleteSnapshotRoundTrip().GetAwaiter().GetResult()),
            ("TCP cargo protocol five rejection", () => CargoSessionTests.CargoRejectsProtocolFive().GetAwaiter().GetResult()),
            ("TCP cargo production adapter independent bags and non-bag revisions", () => CargoInventoryControllerTests.IndependentBagsAndNonBagRevisionChanges().GetAwaiter().GetResult()),
            ("TCP cargo production adapter retains confirmed and unknown on disconnect", () => CargoInventoryControllerTests.DisconnectRetainsConfirmedAndUnknownCargo().GetAwaiter().GetResult()),
            ("guest reference audit rejects shared children despite distinct roots", GuestReferenceAuditTests.DistinctRootsCannotHideSharedChildren),
            ("guest reference audit permits sharing within detached state", GuestReferenceAuditTests.SharingWithinOneSideIsAllowed),
            ("guest reference audit incomplete and excessive traversal rejection", GuestReferenceAuditTests.IncompleteOrExcessiveTraversalCannotPass),
            ("save startup nested reentrant fixed pairing", SaveStartupTraceTests.NestedReentrantCallsKeepFixedPairing),
            ("save startup exception finalizers and missing pairs", SaveStartupTraceTests.ExceptionFinalizersAndMissingPairsLoseEvidence),
            ("save startup queue context and run bounds latch loss", SaveStartupTraceTests.QueueContextAndRunBoundsLatchLossAfterDrain),
            ("save startup Update thread binding and wrong thread rejection", SaveStartupTraceTests.ThreadBindingRequiresActualUpdateMarkerAndRejectsWrongThread),
            ("save startup stopped run identity and old completion rejection", SaveStartupTraceTests.StoppedRunsRetainIdentityAndRejectOldCompletions),
            ("save startup late absent and incomplete early boundaries", SaveStartupTraceTests.LateAbsentAndIncompleteEarlyBoundariesNeverGrantLoadSafety),
            ("timeline boundaries and interpolation", TimelineBoundaries),
            ("timeline capacity, ordering and reset", TimelineCapacity),
            ("pose interpolation and quaternion hemisphere", PoseInterpolation),
            ("invalid pose data", InvalidPoses),
            ("network frame numeric round trip", NetworkTests.CodecRoundTrip),
            ("network malformed data rejection", NetworkTests.CodecRejectsInvalidData),
            ("fragmented and truncated stream frames", () => NetworkTests.FragmentedPackets().GetAwaiter().GetResult()),
            ("TCP handshake and bidirectional frames", () => NetworkTests.TcpHandshakeAndFrames().GetAwaiter().GetResult()),
            ("TCP incompatible build rejection", () => NetworkTests.TcpRejectsIncompatibleBuild().GetAwaiter().GetResult()),
            ("TCP concurrent writes", () => NetworkTests.ConcurrentWrites().GetAwaiter().GetResult()),
            ("invalid outbound frame sequence recovery", () => NetworkTests.InvalidSendDoesNotConsumeSequence().GetAwaiter().GetResult()),
            ("TCP replay rejection and disconnect", () => NetworkTests.ReplayAndDisconnect().GetAwaiter().GetResult()),
            ("TCP blocked read cancellation", () => NetworkTests.CancelBlockedRead().GetAwaiter().GetResult()),
            ("scene and world agreement before frames", SessionTests.SceneAgreement),
            ("scene suspension and old epoch cleanup", SessionTests.SceneTransitions),
            ("guest reload establishes new epoch", SessionTests.GuestReload),
            ("session room, player and authority rejection", SessionTests.InvalidSessionPackets),
            ("heartbeat round trip and process clock offset", SessionTests.ClockEstimate),
            ("bounded frame mailboxes and DTO ownership", SessionTests.MailboxAndOwnership),
            ("session timeouts and queue bounds", SessionTests.TimeoutsAndQueueBounds),
            ("LAN session scene, movement and graceful leave", () => SessionTests.LanSessionLifecycle().GetAwaiter().GetResult()),
            ("LAN scene mismatch timeout", () => SessionTests.SceneMismatchTimeout().GetAwaiter().GetResult()),
            ("LAN silent handshake timeout", () => SessionTests.SilentHandshakeTimeout().GetAwaiter().GetResult()),
            ("LAN listening and connected cancellation", () => SessionTests.CancelListeningAndConnected().GetAwaiter().GetResult()),
            ("sprite asset key culture and signed zero stability", AssetTests.StableKeys),
            ("sprite asset key descriptor identity", AssetTests.DistinctDescriptors),
            ("sprite asset invalid descriptor rejection", AssetTests.InvalidDescriptors),
            ("sprite asset metadata collision rejection", AssetTests.RegistryCollisions),
            ("sprite asset capacity and scene reset", AssetTests.RegistryBoundsAndClear),
            ("layout ordering and multiplicity", WorldAndMotionTests.LayoutOrderAndMultiplicity),
            ("layout geometry and field boundaries", WorldAndMotionTests.LayoutGeometryAndFieldBoundaries),
            ("layout invalid and excessive input rejection", WorldAndMotionTests.LayoutRejectsInvalidInput),
            ("remote motion clock anchoring and staleness", WorldAndMotionTests.MotionClockAndInterpolation),
            ("remote motion epoch, capacity and reset", WorldAndMotionTests.MotionEpochCapacityAndReset),
            ("remote motion malformed data rejection", WorldAndMotionTests.MotionRejectsInvalidData),
            ("host entity identity and pool reuse", EntityWorldTests.EntityIdentityAndReuse),
            ("host entity capacity and replacement", EntityWorldTests.RegistryCapacity),
            ("host entity target resolution and unbind", EntityTargetTests.ResolveAndUnbind),
            ("host entity target pool generation and kind replacement", EntityTargetTests.PoolGenerationAndKindReplacement),
            ("host entity target clear and epoch fence", EntityTargetTests.ClearAndEpochFence),
            ("host entity target capacity and inverse consistency", EntityTargetTests.CapacityAndReplacementConsistency),
            ("host entity target snapshot ownership and failure output", EntityTargetTests.SnapshotOwnershipAndFailureOutput),
            ("world codec and malformed entity rejection", EntityWorldTests.CodecAndInvalidEntities),
            ("world atomic assembly and DTO ownership", EntityWorldTests.AtomicAssemblyAndOwnership),
            ("world slice ordering, replacement and empty roster", EntityWorldTests.AssemblyRejectionAndReplacement),
            ("world session authority and epoch cleanup", EntityWorldTests.SessionAuthorityAndEpoch),
            ("world mailbox capacity and player fairness", EntityWorldTests.SnapshotFairnessAndBounds),
            ("TCP world bootstrap, update and removal", () => EntityWorldTests.TcpWorldLifecycle().GetAwaiter().GetResult()),
            ("TCP legacy world protocol rejection", () => EntityWorldTests.RejectLegacyProtocol().GetAwaiter().GetResult()),
            ("fish visual numeric wire and DTO ownership", FishVisualTests.CodecAndOwnership),
            ("fish visual malformed data rejection", FishVisualTests.InvalidVisuals),
            ("fish visual maximum fields fit packet limit", FishVisualTests.MaximumLegalPacketFits),
            ("fish preview clock, interpolation and staleness", FishVisualTests.PreviewInterpolationAndStaleness),
            ("fish preview removal and epoch reset", FishVisualTests.PreviewRemovalAndEpoch),
            ("slow world producer cannot starve atomic commit", EntityWorldTests.SlowWorldProducerCannotStarveCommit),
            ("fish pool cycle between snapshots changes identity", FishLifecycleTests.PoolCycleBetweenSnapshots),
            ("fish destroy and pointer reuse", FishLifecycleTests.DestroyAndPointerReuse),
            ("fish lifecycle bounds and untracked callbacks", FishLifecycleTests.BoundsAndUntrackedCallbacks),
            ("fish lifecycle clear preserves generations", FishLifecycleTests.ClearPreservesGeneration),
            ("fish lifecycle concurrent callback ownership", FishLifecycleTests.ConcurrentCallbacks),
            ("fish target lookup observes active lifecycle generation", FishLifecycleTests.ActiveGenerationLookup),
            ("fish preview viewport and nearest eligible fish", FishVisualTests.PreviewViewportAndNearestSelection),
            ("fish preview identity retention across distance and viewport", FishVisualTests.PreviewSelectionIdentityRetention),
            ("fish preview invalid viewer rejection", FishVisualTests.PreviewInvalidViewer),
            ("fish preview temporary visual loss preserves identity", FishVisualTests.PreviewTemporaryVisualsRetainIdentity),
            ("fish preview manual reselection preserves replay fence", FishVisualTests.PreviewManualReselectionPreservesReplayFence),
            ("fish preview death, capture and roster removal reasons", FishVisualTests.PreviewTerminalSelectionReasons),
            ("fish preview sample diagnosis and stale recovery", FishVisualTests.PreviewSamplingReasonsAndRecovery),
            ("observed host targets ownership and generation", ObservedTargetTests.SnapshotOwnershipAndGeneration),
            ("observed host targets invalid publish is atomic", ObservedTargetTests.InvalidPublishIsAtomic),
            ("observed host targets concurrent readers and capacity", ObservedTargetTests.ConcurrentReadersAndCapacity),
            ("map selection canonical ordering and culture", MapSelectionTests.CanonicalOrderingAndCulture),
            ("map selection identity and field boundaries", MapSelectionTests.SelectionIdentityAndFieldBoundaries),
            ("map selection copy ownership", MapSelectionTests.CopyOwnership),
            ("map selection incomplete and bounded input", MapSelectionTests.IncompleteAndBoundedInput),
            ("map selection duplicate identity rejection", MapSelectionTests.DuplicateIdentityRejection),
            ("map selection invalid strings and numbers", MapSelectionTests.InvalidStringsAndNumbers),
            ("map selection route connectivity and prefab modes", MapSelectionTests.RouteConnectivityAndModes),
            ("map selection maximum bounded manifest", MapSelectionTests.MaximumBoundedManifest),
            ("route candidate is not a complete manifest", MapSelectionTests.RouteCandidateIsNotCompleteManifest),
            ("route candidate chain and boundary rejection", MapSelectionTests.RouteChainAndBoundaryRejection),
            ("route copy ownership and canonical compatibility", MapSelectionTests.RouteCopyOwnershipAndCanonicalCompatibility),
            ("map choice atomic route order and observation-only snapshot", MapChoiceTests.RouteAtomicOrderAndObservationOnly),
            ("map choice invalid assembly and fingerprint never commit", MapChoiceTests.InvalidAssemblyAndFingerprintNeverCommit),
            ("map choice snapshot and ingress copies are owned", MapChoiceTests.SnapshotAndIngressCopiesAreOwned),
            ("map choice scene revision and duplicate guards", MapChoiceTests.ChoiceSceneRevisionAndDuplicateGuards),
            ("map choice generation replacement and retirement fence", MapChoiceTests.GenerationReplacementAndRetirementFence),
            ("map choice same fingerprint new identity and bounded history", MapChoiceTests.SameFingerprintNewIdentityAndBoundedChoiceHistory),
            ("map choice capacity and maximum Unicode frames", MapChoiceTests.ChoiceCapacityAndMaximumUnicodeFrames),
            ("map choice malformed frames and resource modes", MapChoiceTests.MalformedFramesAndResourceModes),
            ("map choice wire payload and protocol five", MapChoiceTransportTests.WirePayloadAndProtocolFive),
            ("map choice preload route atomic round trip", MapChoiceTransportTests.PreloadRouteAtomicRoundTrip),
            ("map choice replaced batch revokes partial route", MapChoiceTransportTests.ReplacedBatchRevokesPartial),
            ("map choice FIFO bounds and explicit retirement", MapChoiceTransportTests.ChoiceFifoBoundsAndRetirement),
            ("map choice direction room and source rejection", MapChoiceTransportTests.DirectionRoomAndSourceSpoofing),
            ("map choice preload survives scene change and closes cleanly", MapChoiceTransportTests.SceneChangeKeepsPreloadAndCloseClears),
            ("map choice control priority and four lane fairness", MapChoiceTransportTests.ControlPriorityAndFourLaneFairness),
            ("map choice stale publication and repeated retirement", MapChoiceTransportTests.StalePublishedChoiceCanceled),
            ("TCP map choice preload round trip and retirement", () => MapChoiceTransportTests.TcpPreloadRoundTripAndRetirement().GetAwaiter().GetResult()),
            ("TCP map choice protocol four rejection", () => MapChoiceTransportTests.RejectProtocolFour().GetAwaiter().GetResult()),
            ("TCP map choice adapter origin floor and room isolation", () => MapChoiceControllerTests.TcpCallbackFloorAndRoomIsolation().GetAwaiter().GetResult()),
            ("TCP map choice adapter natural generations and choice revisions", () => MapChoiceControllerTests.TcpNaturalGenerationsAndChoiceRevisions().GetAwaiter().GetResult()),
            ("TCP map choice adapter unavailable sample retirement and recovery", () => MapChoiceControllerTests.TcpUnavailableSamplesRetireAndRecover().GetAwaiter().GetResult()),
            ("TCP map choice adapter guest observer stop retains host evidence", () => MapChoiceControllerTests.TcpGuestObserverStopRetainsHostEvidence().GetAwaiter().GetResult()),
            ("TCP origin map inventory removal and retired controller replay", () => OriginMapChoiceAdapterTests.TcpInventoryRemovalAndRetiredControllerReplay().GetAwaiter().GetResult()),
            ("TCP origin map run switch failure and old run tombstones", () => OriginMapChoiceAdapterTests.TcpRunSwitchFailureAndOldRunTombstones().GetAwaiter().GetResult()),
            ("TCP origin map scalar forgery atomic rejection", () => OriginMapChoiceAdapterTests.TcpScalarForgeryRejectedAtomically().GetAwaiter().GetResult()),
            ("TCP origin map eight choice budget and copied inventory", () => OriginMapChoiceAdapterTests.TcpEightChoiceBudgetAndCopiedInventory().GetAwaiter().GetResult()),
            ("TCP origin map pending route room floor and new entry", () => OriginMapChoiceAdapterTests.TcpPendingRouteFloorAndNewRoomEntry().GetAwaiter().GetResult()),
            ("TCP origin map run quota preserves tombstones", () => OriginMapChoiceAdapterTests.TcpRunQuotaKeepsTombstones().GetAwaiter().GetResult()),
            ("guest shadow fence before clone and no isolation grant", GuestShadowTransactionTests.InstallationUsesFenceBeforeCloneAndNeverGrantsIsolation),
            ("guest shadow partial install compensation per root", GuestShadowTransactionTests.InstallFailuresCompensateEveryPossiblyWrittenRoot),
            ("guest shadow unknown restore is never redispatched", GuestShadowTransactionTests.UnknownRestoreNeverRepeatsNativeWrites),
            ("guest shadow foreign roots and manager replacement retain fence", GuestShadowTransactionTests.ForeignRootsAndManagerReplacementRemainFenced),
            ("guest shadow quiescence and fence loss retain references", GuestShadowTransactionTests.QuiescenceAndFenceFailureKeepStrongReferences),
            ("guest shadow thread lease and reentrancy guards", GuestShadowTransactionTests.ThreadLeaseAndReentrantCallsCannotReuseSource),
            ("guest shadow cleanup failures never release early", GuestShadowTransactionTests.CleanupFailuresNeverRedispatchOrReleaseEarly),
            ("guest shadow owned partial cache compensation precedes save roots", GuestShadowTransactionTests.OwnedMixedCacheIsCompensatedBeforeSaveRoots),
            ("guest shadow unknown and foreign cache results cannot overwrite or retry", GuestShadowTransactionTests.CacheUnknownAndForeignResultsCannotOverwriteOrRetry),
            ("guest shadow composite readback cannot authorize single-field restoration", GuestShadowTransactionTests.MixedReadbackIsRejectedForSingleFieldRoots),
            ("guest shadow cache scalar and null pointer cannot supply identity", GuestShadowTransactionTests.CacheScalarAndNullPointerDoNotSupplyIdentity),
            ("guest shadow seventh-cache unknown restore retains both cache owners", GuestShadowTransactionTests.IngameCacheUnknownRestorationRetainsBothCacheOwners),
            ("guest shadow seventh-cache unexplained identity cannot authorize writes", GuestShadowTransactionTests.IngameCacheForeignUnknownAndMixedDoNotAuthorizeWrites),
            ("cargo independent capacities, reservations and native weight", CargoLedgerTests.IndependentCapacityReservationsAndNativeWeight),
            ("cargo operation provenance and atomic product ownership", CargoLedgerTests.OperationProvenanceAndAtomicProducts),
            ("cargo source fence, replay, cancellation and quota", CargoLedgerTests.SourceFenceReplayCancellationAndQuota),
            ("cargo disconnect, scene and room fences", CargoLedgerTests.DisconnectSceneAndRoomFences),
            ("cargo host receipt uses native total and explicit capabilities", CargoLedgerTests.HostReceiptUsesNativeTotalAndCapabilities),
            ("cargo per-item return leases, partial success and host observation", CargoLedgerTests.ReturnPerItemLeasePartialSuccessAndHostObservation),
            ("cargo pending return and abort preserve unknown results", CargoLedgerTests.ReturnPendingCaptureAndAbortKeepUnknown),
            ("cargo canonical fingerprints, copy ownership and invalid bounds", CargoLedgerTests.CanonicalOwnershipAndInvalidInputBounds),
            ("fish world atomic roster and DTO ownership", FishWorldTests.AtomicRosterAndOwnership),
            ("fish world invalid batch preserves committed state", FishWorldTests.InvalidBatchPreservesCommittedWorld),
            ("fish world independent interpolation and visual recovery", FishWorldTests.IndependentInterpolationAndVisualRecovery),
            ("fish world epoch replay and freshness recovery", FishWorldTests.EpochReplayAndFreshnessRecovery),
            ("fish world terminal and undisplayable numeric roster", FishWorldTests.TerminalAndUndisplayableNumericRoster),
            ("fish world capacity and independent history bounds", FishWorldTests.CapacityAndIndependentHistoryBounds),
            ("fish action schema and canonical fingerprint", FishActionTests.SchemaAndCanonicalFingerprint),
            ("fish action bound source and copy ownership", FishActionTests.BoundSourceAndCopyOwnership),
            ("fish action duplicate conflict and replay", FishActionTests.DuplicateConflictAndMonotonicReplay),
            ("fish action business rejection and scene fence", FishActionTests.BusinessRejectionAndSceneFence),
            ("fish action cache eviction and closed room replay", FishActionTests.BoundedCacheAndClosedRoomReplay),
            ("fish action queue rate and arrival freshness", FishActionTests.QueueRateAndArrivalFreshness),
            ("fish action probe and missing authority", FishActionTests.ProbeFactsAndExplicitMissingAuthority),
            ("fish action loadout stage and spatial guards", FishActionTests.FreshLoadoutStageAndSpatialGuards),
            ("fish action target retirement before drain", FishActionTests.TargetRetirementBetweenQueueAndDrain),
            ("fish action dispatch generation and lease", FishActionTests.DispatchRechecksGenerationAndLease),
            ("fish action unknown native outcome never retries", FishActionTests.NativeUnknownNeverDispatchesTwice),
            ("fish action not-started release and target ownership", FishActionTests.NotStartedReleaseAndTargetOwnership),
            ("map origin fixed iterator scopes and unknown parent masking", MapOriginRegistryTests.IteratorScopesStayFixedAndUnknownShadowsParent),
            ("map origin bootstrap and late exact scene completion", MapOriginRegistryTests.BootstrapAndLateSceneCompletionProduceOwnedChoicesOnly),
            ("map origin pointer version handle and entry replay fences", MapOriginRegistryTests.TombstonesRejectPointerVersionHandleAndEntryReplay),
            ("map origin pending birth unload destroy and new entry retirement", MapOriginRegistryTests.PendingBirthCannotReviveAfterUnloadDestroyOrNewEntry),
            ("map origin repeated cache and failed operation retirement", MapOriginRegistryTests.RepeatedCacheAndFailedOperationRetireWithoutRebinding),
            ("map origin thread read loss scope and quota revocation", MapOriginRegistryTests.ThreadReadLossScopeAndQuotaFaultsRevokeAllEvidence),
            ("map origin source owned latest choices and diagnostic queue independence", MapOriginSourceSnapshotTests.SourceSnapshotsOwnLatestChoicesAndPreserveReadyQueue),
            ("map origin source same fingerprint new entry lifetime isolation", MapOriginSourceSnapshotTests.SameFingerprintNewEntryDoesNotMixSourceLives),
            ("map origin source pending exact scene and latest choice after drain", MapOriginSourceSnapshotTests.PendingChoicesWaitForExactSceneAndKeepLatestAfterDrain),
            ("map origin source controller scene and route retirement", MapOriginSourceSnapshotTests.SourceSnapshotsRemoveRetiredControllerSceneAndRouteEvidence),
            ("map origin source fault and pending conflict atomic failure", MapOriginSourceSnapshotTests.FaultAndPendingConflictNeverReturnStaleSource),
            ("map origin source controller capacity and wrong thread revocation", MapOriginSourceSnapshotTests.SourceSnapshotControllerBoundAndWrongThreadFailClosed),
            ("fish action codec and single payload", FishActionTransportTests.CodecAndSinglePayload),
            ("fish action session source ownership", FishActionTransportTests.SessionOwnershipAndProvenance),
            ("fish action authority and forged results", FishActionTransportTests.AuthorityAndForgedResults),
            ("fish action scene invalidation and retired requests", FishActionTransportTests.SceneInvalidationAndRetiredRequests),
            ("fish action scene change between take and publication", FishActionTransportTests.SceneChangeBetweenTakeAndPublication),
            ("fish action FIFO bounds and no overwrite", FishActionTransportTests.FifoBoundsAndNoOverwrite),
            ("fish action result bounds and pending retries", FishActionTransportTests.ResultBoundsAndPendingRetries),
            ("fish action control priority and gameplay fairness", FishActionTransportTests.ControlPriorityAndGameplayFairness),
            ("fish action native entry is not capture success", FishActionTransportTests.NativeEntryCannotBecomeCaptureSuccess),
            ("TCP fish action request result round trip", () => FishActionTransportTests.TcpRequestResultRoundTrip().GetAwaiter().GetResult()),
            ("TCP fish action scene change between take and publication", () => FishActionTransportTests.TcpSceneChangeBetweenTakeAndPublication().GetAwaiter().GetResult()),
            ("TCP fish action protocol three rejection", () => FishActionTransportTests.RejectProtocolThree().GetAwaiter().GetResult())
        };
        int failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void TimelineBoundaries()
    {
        var timeline = new SnapshotTimeline<string>(4);
        Assert(!timeline.TrySample(0, out _, out _, out _), "empty buffer should not sample");
        Assert(timeline.TryPush(10, "a") && timeline.TryPush(12, "b"), "push rejected");
        Assert(timeline.TrySample(11, out string from, out string to, out float alpha), "sample rejected");
        Assert(from == "a" && to == "b" && Math.Abs(alpha - 0.5f) < 0.0001, "wrong interpolation interval");
        timeline.TrySample(0, out from, out to, out alpha);
        Assert(from == "a" && to == "a" && alpha == 0, "before range should clamp");
        timeline.TrySample(100, out from, out to, out alpha);
        Assert(from == "b" && to == "b" && alpha == 0, "after range should freeze");
        Assert(!timeline.TrySample(double.NaN, out _, out _, out _), "NaN query accepted");
    }

    private static void TimelineCapacity()
    {
        var timeline = new SnapshotTimeline<int>(2);
        timeline.TryPush(1, 1); timeline.TryPush(2, 2); timeline.TryPush(3, 3); timeline.TryPush(4, 4);
        Assert(timeline.Count == 2, "buffer not bounded");
        timeline.TrySample(0, out int from, out int to, out _);
        Assert(from == 3 && to == 3, "old frame retained after wrap");
        timeline.TrySample(3.5, out from, out to, out float alpha);
        Assert(from == 3 && to == 4 && Math.Abs(alpha - 0.5f) < 0.0001, "wrapped interpolation wrong");
        Assert(!timeline.TryPush(4, 99) && !timeline.TryPush(2, 99), "duplicate/out-of-order frame accepted");
        Assert(!timeline.TryPush(double.PositiveInfinity, 99), "non-finite timestamp accepted");
        timeline.Clear();
        Assert(timeline.Count == 0 && timeline.TryPush(0, 7), "reset did not accept new scene clock");
        timeline.TrySample(0, out from, out to, out _);
        Assert(from == 7 && to == 7, "previous scene leaked into new buffer");
    }

    private static void PoseInterpolation()
    {
        Pose from = MakePose(Vector3.Zero, Quaternion.Identity);
        Pose to = MakePose(new Vector3(10, 4, 2), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
        Pose halfway = Pose.Interpolate(from, to, 0.5f);
        Assert(Vector3.Distance(halfway.Position, new Vector3(5, 2, 1)) < 0.0001, "wrong position");
        Vector3 facing = Vector3.Transform(Vector3.UnitX, halfway.Rotation);
        Assert(Math.Abs(facing.X - facing.Y) < 0.0001 && facing.X > 0.7, "wrong rotation interpolation");
        to.Rotation = new Quaternion(0, 0, 0, -1);
        halfway = Pose.Interpolate(from, to, 0.5f);
        Assert(halfway.IsValid() && Vector3.Distance(Vector3.Transform(Vector3.UnitX, halfway.Rotation), Vector3.UnitX) < 0.0001,
            "equivalent quaternion signs caused a spin");
    }

    private static void InvalidPoses()
    {
        Pose pose = MakePose(Vector3.Zero, Quaternion.Identity);
        Assert(pose.IsValid(), "identity pose invalid");
        pose.Position = new Vector3(float.NaN, 0, 0);
        Assert(!pose.IsValid(), "NaN position accepted");
        pose = MakePose(Vector3.Zero, new Quaternion(0, 0, 0, 0));
        Assert(!pose.IsValid(), "zero quaternion accepted");
        pose = MakePose(Vector3.Zero, new Quaternion(float.MaxValue, 0, 0, 1));
        Assert(!pose.IsValid(), "overflowing quaternion accepted");
    }

    private static Pose MakePose(Vector3 position, Quaternion rotation) => new Pose
    {
        Position = position, Rotation = rotation, Scale = Vector3.One
    };

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
