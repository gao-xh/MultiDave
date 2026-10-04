using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.World;
using DaveCoop.Rendering;
using UnityEngine;

namespace DaveCoop.Networking
{
    internal sealed class NetworkController : IDisposable
    {
        private sealed class Peers : IDisposable
        {
            public SessionPeer Main;
            public SessionPeer Loopback;
            public void Dispose() { Main?.Dispose(); Loopback?.Dispose(); }
        }

        private readonly LocalAvatarCapture _local = new LocalAvatarCapture();
        private readonly SpriteCatalog _catalog = new SpriteCatalog();
        private readonly NetworkAvatarRenderer _display = new NetworkAvatarRenderer();
        private readonly FishStateCapture _fish = new FishStateCapture();
        private readonly FishLifecycleHooks _fishLifecycle = new FishLifecycleHooks();
        private readonly SpineCatalog _spines = new SpineCatalog();
        private readonly RemoteFishPreview _fishPreview = new RemoteFishPreview();
        private readonly RemoteFishWorld _fishWorld = new RemoteFishWorld();
        private readonly FishInteractionHooks _fishInteractions = new FishInteractionHooks();
        private MapSelectionCapture _mapSelection;
        private readonly MapSelectionHooks _mapSelectionHooks = new MapSelectionHooks();
        private MapSelectionHookCapture _mapCalls;
        private bool _mapHooksBlocked;
        private string _mapHookWarning;
        private float _nextMapObserverLog;
        private readonly FishActionController _fishActions = new FishActionController();
        private readonly MapChoiceController _mapChoices = new MapChoiceController();
        private readonly CargoInventoryController _cargoInventory = new CargoInventoryController();
        private HostFishInterestSource _hostFishInterest;
        private NativeHostFishAllocatorArea _hostFishAllocatorArea;
        private NativeHostFishLodArea _hostFishLodArea;
        private NativeHostFishVisibilityArea _hostFishVisibilityArea;
        private bool _hostFishAreasFailed;
        private float _nextCargoObservation;
        private LootObservationController _lootObserver;
        private MapOriginController _mapOrigins;
        private long _lastMapCopyDropped;
        private long _lastMapUnexpectedThreads;
        private long _lastMapReadErrors;
        private long _lastMapHookDropped;
        private string _lastRouteInputTrace;
        private float _nextRouteInputs;
        private int _routeInputsLogged;
        private string _routeInputWarning;
        private string _lastMapSelection;
        private string _mapSelectionStatus;
        private float _nextMapSelection;
        private string _fishWorldWarning;
        private string _interactionWarning;
        private long _interactionEvents;
        private long _mappedInteractionEvents;
        private long _nativeInteractionTargetsAtDrain;
        private long _nativeInteractionLookupErrors;
        private string _fishPreviewWarning;
        private long _worldEpoch;
        private long _lastRemoteWorldRevision;
        private long _lastPublishedWorldRevision;
        private int _lastRemoteFishCount;
        private float _nextWorldCapture;
        private float _nextWorldLog;
        private string _worldWarning;
        private CancellationTokenSource _attempt;
        private Task<Peers> _pending;
        private Peers _peers;
        private SceneDescriptor _scene;
        private long _displayEpoch;
        private float _nextOwnerCheck;
        private float _nextLayoutCheck;
        private float _nextLog;
        private float _nextCapture;
        private string _portText;
        private string _message = "Create or join a room before diving.";
        private string _lastError;
        private string _layoutMessage;
        private bool _started;
        private bool _panelActive;
        private bool _savedCursorVisible;
        private CursorLockMode _savedCursorLock;

        public void Update()
        {
            try
            {
                if (!_started)
                {
                    _started = true; _portText = NetworkDriver.Port.Value.ToString(CultureInfo.InvariantCulture);
                    _mapSelection = new MapSelectionCapture(Environment.CurrentManagedThreadId);
                    _lootObserver = new LootObservationController(Environment.CurrentManagedThreadId, ResolveHealthyFish);
                    _mapOrigins = new MapOriginController(Environment.CurrentManagedThreadId);
                    if (NetworkDriver.ExperimentalHostFishAreas.Value && NativeGuestInitializationController.Current == null)
                    {
                        _hostFishInterest = new HostFishInterestSource(() => _peers?.Main,
                            () => _peers?.Loopback != null, _local, Environment.CurrentManagedThreadId);
                        _hostFishInterest.ConfirmUnityUpdate();
                        _hostFishAllocatorArea = new NativeHostFishAllocatorArea(_hostFishInterest, NetworkDriver.Logger);
                        _hostFishLodArea = new NativeHostFishLodArea(_hostFishInterest, NetworkDriver.Logger);
                        _hostFishVisibilityArea = new NativeHostFishVisibilityArea(_hostFishInterest, NetworkDriver.Logger);
                        try { _hostFishAllocatorArea.Install(); _hostFishLodArea.Install(); _hostFishVisibilityArea.Install(); }
                        catch (Exception error) { StopHostFishAreas(error); }
                    }
                    NetworkDriver.Logger.LogInfo(NativeGuestInitializationController.Current == null
                        ? "DAVECOOP_NETWORK_READY: F11 opens LAN movement test panel; no automatic connection."
                        : "DAVECOOP_NETWORK_READY: Guest startup mode is automatically joining the configured host.");
                    if (NativeGuestInitializationController.Current != null)
                        Start("guest");
                }
                if (Input.GetKeyDown(KeyCode.F11)) NetworkDriver.ShowPanel.Value = !NetworkDriver.ShowPanel.Value;
                if (NetworkDriver.ShowPanel.Value && !_panelActive)
                {
                    _savedCursorVisible = Cursor.visible; _savedCursorLock = Cursor.lockState; _panelActive = true;
                }
                if (!NetworkDriver.ShowPanel.Value) RestoreCursor();
                _hostFishInterest?.ConfirmUnityUpdate();
                ObserveMapSelectionCalls();
                ObserveRouteInputs();
                _lootObserver.Update(NetworkDriver.ObserveLootCalls.Value, Time.unscaledTime);
                bool temporaryGuest = NativeGuestInitializationController.Current != null;
                bool hostMapSource = _peers != null && _peers.Main.Snapshot.Role == SessionRole.Host &&
                    _peers.Main.Snapshot.Phase != SessionPhase.Closed;
                // The host producer starts before a fresh dive. Experimental
                // guest replacements cannot be mistaken for original results
                // by the independent read-only origin observer.
                _mapOrigins.Update(!temporaryGuest && (NetworkDriver.ObserveMapOrigins.Value || hostMapSource), Time.unscaledTime);
                FinishAttempt();
                if (_peers == null) { _hostFishInterest?.Clear("NoCurrentPeer"); return; }
                SessionSnapshot state = _peers.Main.Snapshot;
                while (_peers.Main.TryTakeEvent(out SessionEvent item))
                    NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_EVENT: " + JsonSerializer.Serialize(item));
                if (state.Phase == SessionPhase.Closed)
                {
                    string reason = state.Reason; Disconnect(); _message = reason; NetworkDriver.Status = "Network: " + reason; return;
                }
                _mapChoices.ObserveOrigin(_mapOrigins.CaptureSourceFrame(), _peers.Main);
                _mapChoices.Update(_peers.Main, _peers.Loopback);
                if (Time.unscaledTime >= _nextCargoObservation)
                {
                    _nextCargoObservation = Time.unscaledTime + 0.25f;
                    _cargoInventory.Update(_peers.Main, _peers.Loopback);
                }
                UpdateLocalScene();
                state = _peers.Main.Snapshot;
                if (!FishPreviewDisplayEnabled(state)) _fishPreview.Clear("PreviewDisplayUnavailable");
                if (!HostFishObservationsEnabled(state)) _fishLifecycle.Dispose();
                if (state.Role != SessionRole.Host || state.Phase != SessionPhase.Ready ||
                    !NetworkDriver.TransmitFishObservations.Value || !NetworkDriver.ObserveFishInteractions.Value) _fishInteractions.Dispose();
                if (state.Phase != SessionPhase.Ready || state.SceneEpoch != _worldEpoch)
                {
                    _fish.Clear(); _fishLifecycle.ClearObserved(); _lastRemoteWorldRevision = 0; _lastPublishedWorldRevision = 0; _lastRemoteFishCount = 0;
                    _fishInteractions.ClearPending(); _fishWorld.Clear("ScenePausedOrChanged");
                    _fishActions.InvalidateScene();
                    _mapSelection.Clear(); _lastMapSelection = null; _mapSelectionStatus = null;
                    _fishPreview.Clear("ScenePausedOrChanged"); _spines.Clear();
                    _worldEpoch = state.Phase == SessionPhase.Ready ? state.SceneEpoch : 0;
                }
                if (state.Phase != SessionPhase.Ready || state.SceneEpoch != _displayEpoch)
                {
                    _display.Clear(); _displayEpoch = state.Phase == SessionPhase.Ready ? state.SceneEpoch : 0;
                }
                if (_peers.Main.TryTakeRemoteFrame(out ReceivedFrame received))
                {
                    if (!_hostFishAreasFailed) _hostFishInterest?.Accept(_peers.Main, received);
                    if (state.Phase == SessionPhase.Ready && received.Frame.SceneEpoch == state.SceneEpoch)
                        _display.Receive(received);
                }
                UpdateHostFishAreas();
                // The internal peer is also pure CLR. Drain its mailbox so the
                // diagnostic exercises both directions without touching Unity there.
                if (_peers.Loopback != null) _peers.Loopback.TryTakeRemoteFrame(out _);
                SessionPeer worldReceiver = _peers.Loopback ?? _peers.Main;
                if (worldReceiver.TryTakeRemoteWorld(out WorldSnapshot world) && world.SceneEpoch == state.SceneEpoch && state.Phase == SessionPhase.Ready)
                {
                    _lastRemoteWorldRevision = world.Revision; _lastRemoteFishCount = world.Entities.Length;
                    if (FishPreviewDisplayEnabled(state))
                    {
                        _fishPreview.Receive(world, worldReceiver.Now, _local, _peers.Loopback != null);
                        if (!FishPreviewDisplayEnabled(_peers.Main.Snapshot)) _fishPreview.Clear("GuestFishSourceChanged");
                    }
                    else _fishPreview.Clear("PreviewDisplayUnavailable");
                    if (FishWorldDisplayEnabled(state))
                    {
                        _fishWorld.Receive(world, worldReceiver.Now);
                        if (!FishWorldDisplayEnabled(_peers.Main.Snapshot)) _fishWorld.Clear("GuestFishSourceChanged");
                    }
                    else _fishWorld.Clear("WorldDisplayUnavailable");
                    if (Time.unscaledTime >= _nextWorldLog)
                    {
                        _nextWorldLog = Time.unscaledTime + 2f;
                        NetworkDriver.Logger.LogInfo("DAVECOOP_WORLD_RECEIVED: " + JsonSerializer.Serialize(new
                        {
                            world.SceneEpoch, world.SceneKey, world.Revision, world.SampleTime,
                            FishCount = world.Entities.Length, DiagnosticOnly = true
                        }));
                    }
                }
                if (!_fishLifecycle.Healthy) _fish.Clear();
                DrainFishInteractions();
                _fishActions.Update(_peers.Main, _peers.Loopback, _fish, _fishLifecycle, _lastPublishedWorldRevision);
                string mode = _peers.Loopback == null ? state.Role.ToString() : "Loopback";
                _message = _scene == null && _layoutMessage != null ? _layoutMessage : state.Reason;
                NetworkDriver.Status = $"Network: {mode} / {state.Phase} | RTT {state.RoundTripSeconds * 1000:F0} ms";
                if (Time.unscaledTime >= _nextLog)
                {
                    _nextLog = Time.unscaledTime + 2;
                    NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_STATE: " + JsonSerializer.Serialize(new
                    {
                        Mode = mode, state.Phase, state.SceneEpoch, state.SceneKey, state.HasClockEstimate,
                        state.RemoteRequestsHostFishDisplay,
                        HostFishAreasEnabled = _hostFishInterest != null,
                        HostFishAreasFailed = _hostFishAreasFailed,
                        HostFishInterestStatus = _hostFishInterest?.Status,
                        HostFishInterestAccepted = _hostFishInterest?.AcceptedReceipts ?? 0,
                        HostFishInterestRejected = _hostFishInterest?.RejectedReceipts ?? 0,
                        HostFishAllocatorAreaStatus = _hostFishAllocatorArea?.Status,
                        HostFishAllocatorProxyReturns = _hostFishAllocatorArea?.ProxyReturns ?? 0,
                        HostFishLodAreaStatus = _hostFishLodArea?.Status,
                        HostFishLodBoundRecords = _hostFishLodArea?.BoundRecords ?? 0,
                        HostFishLodJoinedBatches = _hostFishLodArea?.JoinedBatches ?? 0,
                        HostFishLodAppliedRows = _hostFishLodArea?.AppliedRows ?? 0,
                        HostFishLodUnsupportedTargets = _hostFishLodArea?.UnsupportedTargets ?? 0,
                        HostFishLodUnsupportedRows = _hostFishLodArea?.UnsupportedRows ?? 0,
                        HostFishLodUnknownWriteOutcomes = _hostFishLodArea?.UnknownWriteOutcomes ?? 0,
                        HostFishVisibilityAreaStatus = _hostFishVisibilityArea?.Status,
                        HostFishVisibilityProxyReturns = _hostFishVisibilityArea?.ProxyReturns ?? 0,
                        HostFishVisibilityUnsupported = _hostFishVisibilityArea?.Unsupported ?? 0,
                        HostFishVisibilityMissingInterest = _hostFishVisibilityArea?.MissingInterest ?? 0,
                        HostFishVisibilityPendingScopes = _hostFishVisibilityArea?.PendingScopes ?? 0,
                        HostFishVisibilityCallbackErrors = _hostFishVisibilityArea?.CallbackErrors ?? 0,
                        AutomaticHostFishObservation = state.Role == SessionRole.Host && state.RemoteRequestsHostFishDisplay,
                        GuestQuarantinedFish = NativeGuestInitializationController.Current?.QuarantinedFishCount ?? 0,
                        GuestFishStatus = NativeGuestInitializationController.Current?.GuestFishStatus,
                        state.RoundTripSeconds, LocalPlayer = _local.PlayerId, LocalParts = _local.PartCount,
                        UnkeyedLocalParts = _local.UnkeyedVisibleParts, SkippedDestroyedLocalParts = _local.SkippedDestroyedParts, RemoteVisibleParts = _display.VisibleParts,
                        RemoteUnknownAssets = _display.UnknownAssets, RemoteHistoryCount = _display.HistoryCount,
                        LocalHarpoonVisualParts = _local.HarpoonVisualParts,
                        LocalHarpoonVisualVisibleParts = _local.HarpoonVisualVisibleParts,
                        LocalHarpoonVisualDuplicateParts = _local.HarpoonVisualDuplicateParts,
                        LocalHarpoonVisualUnkeyedParts = _local.HarpoonVisualUnkeyedParts,
                        LocalHarpoonVisualReadErrors = _local.HarpoonVisualReadErrors,
                        LocalHarpoonVisualCapacitySkips = _local.HarpoonVisualCapacitySkips,
                        LocalHarpoonVisualStatus = _local.HarpoonVisualStatus,
                        RemoteHarpoonVisualRenderErrors = _display.HarpoonRenderErrors,
                        ObservedHostFish = _fish.ObservedFish, HostFishBindableTargets = _fish.BindableTargets, _fish.UninitializedFish,
                        _fish.UnresolvedVisuals, _fish.FirstVisualError, RemoteWorldRevision = _lastRemoteWorldRevision, RemoteFishCount = _lastRemoteFishCount,
                        FishPreviewEntity = _fishPreview.SelectedEntity, FishPreviewVisible = _fishPreview.Visible, FishPreviewUnknownResource = _fishPreview.UnknownResource,
                        FishPreviewInView = _fishPreview.InView, FishPreviewMeshVertices = _fishPreview.MeshVertices,
                        FishPreviewStatus = _fishPreview.DisplayStatus, FishPreviewSnapshotAge = _fishPreview.SnapshotAge,
                        FishLifecycleHooks = _fishLifecycle.Installed, FishLifecycleTracked = _fishLifecycle.Tracker.Count,
                        FishLifecycleTransitions = _fishLifecycle.Tracker.Transitions, FishLifecycleCallbackErrors = _fishLifecycle.CallbackErrors,
                        FishWorldReceived = _fishWorld.ReceivedFishCount, FishWorldAlive = _fishWorld.AliveFishCount,
                        FishWorldRenderable = _fishWorld.RenderableCount, FishWorldVisible = _fishWorld.VisibleCount,
                        FishWorldInView = _fishWorld.InViewCount, FishWorldUnknownResource = _fishWorld.UnknownResourceCount,
                        FishWorldMissingVisual = _fishWorld.MissingVisualCount, FishWorldSourceInvisible = _fishWorld.SourceInvisibleCount,
                        FishWorldNodes = _fishWorld.NodeCount, FishWorldMeshVertices = _fishWorld.MeshVertices,
                        FishWorldStatus = _fishWorld.DisplayStatus, FishWorldSnapshotAge = double.IsFinite(_fishWorld.SnapshotAge) ? (double?)_fishWorld.SnapshotAge : null,
                        FishWorldRenderErrors = _fishWorld.RenderErrorCount,
                        FishInteractionHooks = _fishInteractions.Installed, FishInteractionHealthy = _fishInteractions.Healthy,
                        FishInteractionEvents = _interactionEvents, FishInteractionMappedEvents = _mappedInteractionEvents,
                        FishInteractionDropped = _fishInteractions.Dropped, FishInteractionCallbackErrors = _fishInteractions.CallbackErrors,
                        FishInteractionResolverErrors = _fishInteractions.ResolverErrors,
                        FishInteractionUnmatchedAfter = _fishInteractions.UnmatchedAfterCallbacks,
                        FishInteractionPendingCalls = _fishInteractions.PendingCalls,
                        FishInteractionProcessLimitReached = _fishInteractions.ProcessLimitReached,
                        FishInteractionNativeTargetsAtDrain = _nativeInteractionTargetsAtDrain,
                        FishInteractionNativeLookupErrors = _nativeInteractionLookupErrors,
                        MapSelectionFingerprint = _lastMapSelection, MapSelectionStatus = _mapSelectionStatus,
                        MapSelectionHooks = _mapSelectionHooks.Installed, MapSelectionHooksHealthy = _mapSelectionHooks.Healthy,
                        MapSelectionCallbackErrors = _mapSelectionHooks.CallbackErrors,
                        MapChoicePublishedRoutes = _mapChoices.PublishedRoutes, MapChoicePublishedChoices = _mapChoices.PublishedChoices,
                        MapChoiceReceivedSnapshots = _mapChoices.ReceivedSnapshots, MapChoiceUnboundChoices = _mapChoices.UnboundChoices,
                        MapChoiceRemoteGeneration = _mapChoices.RemoteGeneration, MapChoiceRemoteRouteScenes = _mapChoices.RemoteRouteSceneCount,
                        MapChoiceRemoteChoices = _mapChoices.RemoteChoiceCount, HostMapSelectionApplied = false,
                        ExperimentalGuestInstalledRoutes = NativeGuestInitializationController.Current?.MapController.InstalledRoutes ?? 0,
                        ExperimentalGuestMapStatus = NativeGuestInitializationController.Current?.MapController.Status,
                        ExperimentalGuestSuppliedIgpChoices = NativeGuestInitializationController.Current?.MapController.SuppliedIgpChoices ?? 0,
                        ExperimentalGuestIgpStatus = NativeGuestInitializationController.Current?.MapController.IgpStatus,
                        MapChoiceOriginRunId = _mapChoices.SourceOriginRunId,
                        MapChoiceOriginOwnerLife = _mapChoices.SourceOriginOwnerLife,
                        MapChoiceOriginPending = _mapChoices.PendingOriginChoices,
                        MapChoiceLegacySuppressed = _mapChoices.SuppressedLegacyObservations,
                        LootObservationHooks = _lootObserver.Installed, LootObservationHealthy = _lootObserver.Healthy,
                        LootObservationEvents = _lootObserver.Events, LootObservationReadErrors = _lootObserver.ReadErrors,
                        LootObservationUnexpectedThreads = _lootObserver.UnexpectedThreads,
                        MapOriginHooks = _mapOrigins.Installed, MapOriginHealthy = _mapOrigins.Healthy,
                        MapOriginEvents = _mapOrigins.Events, MapOriginBoundChoices = _mapOrigins.BoundChoices,
                        MapOriginReadErrors = _mapOrigins.ReadErrors, MapOriginUnexpectedThreads = _mapOrigins.UnexpectedThreads,
                        NativeMapGenerationVerified = false,
                        CargoGameplayEnabled = false, EmployeeBagDiversionEnabled = false, EmployeeStorageBridgeEnabled = false,
                        CargoLedgerEvidenceAttached = _cargoInventory.HostLedgerAttached,
                        CargoLedgerEvidenceRetained = _cargoInventory.HostLedgerRetained,
                        CargoInventoryPublishedSnapshots = _cargoInventory.PublishedSnapshots,
                        CargoInventoryReceivedSnapshots = _cargoInventory.ReceivedSnapshots,
                        CargoInventoryRemoteRevision = _cargoInventory.RemoteRevision,
                        CargoInventoryRemoteTrackedProducts = _cargoInventory.RemoteTrackedProducts,
                        CargoInventoryObservationOnly = true, NativeBagInventoryComplete = false,
                        FishActionQueued = _fishActions.PendingCount, FishActionHighestRequestId = _fishActions.HighestRequestId,
                        FishActionReceived = _fishActions.ReceivedRequests, FishActionResults = _fishActions.ReceivedResults,
                        FishActionNativeLookupErrors = _fishActions.NativeLookupErrors,
                        FishActionNativeDispatches = 0
                    }));
                }
            }
            catch (Exception error) { Fail(error); }
        }

        public void LateUpdate()
        {
            if (_panelActive) { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
            if (_peers == null || _scene == null || !_local.IsAvailable) return;
            try
            {
                SessionSnapshot state = _peers.Main.Snapshot;
                if (state.Phase != SessionPhase.Ready) return;
                ObserveWorld(state);
                ObserveMapSelection();
                ObserveFishInteractions();
                if (Time.unscaledTime >= _nextCapture)
                {
                    _nextCapture = Time.unscaledTime + 1f / 30;
                    PlayerFrame frame = _local.Capture(_peers.Main.Now, _catalog);
                    if (_local.SkippedDestroyedParts > 0)
                    {
                        _nextOwnerCheck = 0;
                        NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_PARTS_STALE: skipped destroyed display slots; refresh scheduled; session retained.");
                    }
                    _peers.Main.PublishFrame(frame);
                    if (_peers.Loopback != null && _peers.Loopback.Snapshot.Phase == SessionPhase.Ready)
                    {
                        frame.SampleTime = _peers.Loopback.Now;
                        var root = frame.Root; root.Position += new System.Numerics.Vector3(3, 0, 0); frame.Root = root;
                        _peers.Loopback.PublishFrame(frame);
                    }
                }
                float delay = NetworkDriver.RenderDelay.Value;
                if (!float.IsFinite(delay)) delay = 0.12f;
                _display.Render(_peers.Main.Now, Math.Clamp(delay, 0.05f, 0.5f), _local, _catalog);
                if (FishPreviewDisplayEnabled(state))
                {
                    try
                    {
                        _fishPreview.Render((_peers.Loopback ?? _peers.Main).Now, Math.Clamp(delay, 0.05f, 0.5f), _local, _catalog, _spines, _peers.Loopback != null);
                        if (!FishPreviewDisplayEnabled(_peers.Main.Snapshot)) _fishPreview.Clear("GuestFishSourceChanged");
                        _fishPreviewWarning = null;
                    }
                    catch (Exception error)
                    {
                        string message = error.GetType().Name + ": " + error.Message;
                        if (message != _fishPreviewWarning) NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_PREVIEW_WARNING: " + message);
                        _fishPreviewWarning = message; _fishPreview.Clear("Exception");
                    }
                }
                else _fishPreview.Clear("PreviewDisplayUnavailable");
                _fishPreview.Trace();
                if (FishWorldDisplayEnabled(state))
                {
                    try
                    {
                        _fishWorld.Render((_peers.Loopback ?? _peers.Main).Now, Math.Clamp(delay, 0.05f, 0.5f), _local, _catalog, _spines, _peers.Loopback != null);
                        if (!FishWorldDisplayEnabled(_peers.Main.Snapshot)) _fishWorld.Clear("GuestFishSourceChanged");
                        _fishWorldWarning = null;
                    }
                    catch (Exception error)
                    {
                        string message = error.GetType().Name + ": " + error.Message;
                        if (message != _fishWorldWarning) NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_WORLD_WARNING: " + message);
                        _fishWorldWarning = message; _fishWorld.Clear("Exception");
                    }
                }
                else _fishWorld.Clear("WorldDisplayUnavailable");
            }
            catch (Exception error) { Fail(error); }
        }

        private void UpdateLocalScene()
        {
            if (Time.unscaledTime >= _nextOwnerCheck)
            {
                _nextOwnerCheck = Time.unscaledTime + 0.5f;
                InGameManager manager = LocalAvatarCapture.FindLocalManager();
                if (manager == null || manager.playerCharacter == null)
                {
                    if (_local.PlayerId != 0) ClearLocal();
                    return;
                }
                PlayerCharacter player = manager.playerCharacter;
                if (_local.PlayerId != player.GetInstanceID() || _local.SceneHandle != player.gameObject.scene.handle)
                {
                    ClearLocal(); _local.Bind(manager); _nextLayoutCheck = 0;
                }
                else if (_local.IsAvailable && !_local.VisualPartsMatch())
                {
                    _local.RefreshVisualParts(); _catalog.Clear(); _display.Clear(); _displayEpoch = 0;
                    NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_PARTS_CHANGED: " + _local.PartCount);
                }
            }
            if (!_local.IsAvailable)
            {
                if (_scene != null) { _scene = null; SetScene(null); _display.Clear(); }
                return;
            }
            if (_scene == null && Time.unscaledTime >= _nextLayoutCheck)
            {
                _nextLayoutCheck = Time.unscaledTime + 1;
                try
                {
                    _scene = WorldLayoutReader.Read(_local.Manager);
                    if (_scene != null)
                    {
                        _catalog.ScanLoadedSprites(Time.unscaledTime);
                        SetScene(_scene);
                        NetworkDriver.Logger.LogInfo("DAVECOOP_LAYOUT_READY: " + JsonSerializer.Serialize(new
                        {
                            _scene.Key, _scene.WorldFingerprint, SourceId = _local.PlayerId, Parts = _local.PartCount
                        }));
                    }
                }
                catch (Exception error)
                {
                    _layoutMessage = "Layout agreement unavailable: " + error.Message;
                    if (_lastError != _layoutMessage) { _lastError = _layoutMessage; NetworkDriver.Logger.LogWarning("DAVECOOP_LAYOUT_WARNING: " + error.Message); }
                }
            }
        }

        private void SetScene(SceneDescriptor scene)
        {
            _peers?.Main.SetLocalScene(scene); _peers?.Loopback?.SetLocalScene(scene);
        }

        private void ClearLocal()
        {
            _hostFishInterest?.Clear("LocalPlayerChanged");
            _scene = null; _layoutMessage = null; SetScene(null); _local.Clear(); _catalog.Clear(); _display.Clear(); _displayEpoch = 0;
            _fish.Clear(); _fishLifecycle.ClearObserved(); _worldEpoch = 0; _lastRemoteWorldRevision = 0; _lastRemoteFishCount = 0;
            _fishPreview.Clear(); _spines.Clear();
            _fishWorld.Clear(); _fishInteractions.ClearPending(); _mapSelection?.Clear(); _lastMapSelection = null; _mapSelectionStatus = null;
        }

        private void ObserveMapSelection()
        {
            if (!NetworkDriver.TransmitFishObservations.Value || Time.unscaledTime < _nextMapSelection) return;
            _nextMapSelection = Time.unscaledTime + 1f;
            try
            {
                MapSelectionManifest manifest = _mapSelection.Capture(_local.Manager);
                if (manifest == null)
                {
                    _mapSelectionStatus = _mapSelection.UnavailableReason;
                    if (_lastMapSelection != null) _lastMapSelection = null;
                    return;
                }
                string fingerprint = MapSelections.Fingerprint(manifest);
                _mapSelectionStatus = "Ready";
                if (_lastMapSelection == fingerprint) return;
                _lastMapSelection = fingerprint;
                NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_SELECTION: " + JsonSerializer.Serialize(new
                {
                    _worldEpoch, fingerprint, manifest.EntrySceneId,
                    SceneCount = manifest.Scenes.Length, GroupCount = manifest.Groups.Length,
                    Scenes = manifest.Scenes, Groups = manifest.Groups,
                    PostLoadObservationOnly = true, HostSelectionApplied = false
                }));
            }
            catch (Exception error)
            {
                string message = error.GetType().Name + ": " + error.Message;
                if (_mapSelectionStatus != message) NetworkDriver.Logger.LogWarning("DAVECOOP_MAP_SELECTION_WARNING: " + message);
                _mapSelectionStatus = message; _lastMapSelection = null;
            }
        }

        private void ObserveRouteInputs()
        {
            if (!NetworkDriver.TransmitFishObservations.Value || Time.unscaledTime < _nextRouteInputs || _routeInputsLogged >= 1024) return;
            _nextRouteInputs = Time.unscaledTime + 1f;
            try
            {
                MapRouteObservation inputs = _mapSelection.ReadRouteInputs();
                string signature = inputs.TraceKey();
                if (signature == _lastRouteInputTrace) return;
                _lastRouteInputTrace = signature; _routeInputWarning = null; _routeInputsLogged++;
                NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_ROUTE_INPUTS: " + JsonSerializer.Serialize(inputs));
            }
            catch (Exception error)
            {
                string message = error.GetType().Name + ": " + error.Message;
                if (_routeInputWarning != message && _routeInputsLogged++ < 1024)
                    NetworkDriver.Logger.LogWarning("DAVECOOP_MAP_ROUTE_INPUTS_WARNING: " + message);
                _routeInputWarning = message;
            }
        }

        private void ObserveMapSelectionCalls()
        {
            if (!NetworkDriver.ObserveMapSelectionCalls.Value || NativeGuestInitializationController.Current != null)
            {
                StopMapSelectionCalls(); return;
            }
            if (_mapHooksBlocked) return;
            try
            {
                if (_mapCalls == null) _mapCalls = new MapSelectionHookCapture(Environment.CurrentManagedThreadId);
                bool wasInstalled = _mapSelectionHooks.Installed;
                _mapSelectionHooks.Enable(_mapCalls.Capture);
                _mapSelectionHooks.CheckHealthy();
                if (!wasInstalled) NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_SELECTION_HOOKS_READY: five read-only call observers installed; original arguments/results unchanged.");
                if (_mapCalls.Dropped > _lastMapCopyDropped || _mapCalls.UnexpectedThreads > _lastMapUnexpectedThreads ||
                    _mapCalls.ReadErrors > _lastMapReadErrors || _mapSelectionHooks.Dropped > _lastMapHookDropped)
                    NetworkDriver.Logger.LogWarning("DAVECOOP_MAP_SELECTION_TRACE_INCOMPLETE: legacy call diagnostics lost evidence; fixed-origin map source is independent.");
                _lastMapCopyDropped = _mapCalls.Dropped; _lastMapUnexpectedThreads = _mapCalls.UnexpectedThreads;
                _lastMapReadErrors = _mapCalls.ReadErrors; _lastMapHookDropped = _mapSelectionHooks.Dropped;
                int drained = 0;
                while (drained++ < 16 && _mapCalls.TryTake(out MapSelectionCallObservation observation))
                {
                    NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_SELECTION_CALL: " + JsonSerializer.Serialize(observation));
                    _mapChoices.Observe(observation, _peers?.Main);
                }
                if (Time.unscaledTime >= _nextMapObserverLog)
                {
                    _nextMapObserverLog = Time.unscaledTime + 2;
                    NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_SELECTION_OBSERVER_STATE: " + JsonSerializer.Serialize(new
                    {
                        _mapSelectionHooks.Installed, _mapSelectionHooks.Healthy, _mapSelectionHooks.CallbackErrors,
                        _mapSelectionHooks.ProcessAccepted, _mapSelectionHooks.ProcessLimitReached,
                        HookDropped = _mapSelectionHooks.Dropped, _mapSelectionHooks.CleanupVerified,
                        CopyDropped = _mapCalls.Dropped, _mapCalls.UnexpectedThreads, _mapCalls.ReadErrors,
                        ObservationOnly = true, HostSelectionApplied = false
                    }));
                }
            }
            catch (Exception error)
            {
                _mapHooksBlocked = true;
                ReportMapHookError(error);
                StopMapSelectionCalls();
            }
        }

        private void StopMapSelectionCalls()
        {
            bool hadObserver = _mapCalls != null || _mapSelectionHooks.Installed;
            _mapCalls?.Stop(); _mapCalls = null;
            try
            {
                _mapSelectionHooks.Dispose();
                if (hadObserver && _mapSelectionHooks.CleanupVerified)
                    NetworkDriver.Logger.LogInfo("DAVECOOP_MAP_SELECTION_HOOKS_STOPPED: own registrations removed; copied observation queue cleared.");
            }
            catch (Exception error) { _mapHooksBlocked = true; ReportMapHookError(error); }
        }

        private void ReportMapHookError(Exception error)
        {
            string message = error.GetType().Name + ": " + error.Message;
            if (_mapHookWarning != message) NetworkDriver.Logger.LogWarning("DAVECOOP_MAP_SELECTION_HOOK_WARNING: " + message);
            _mapHookWarning = message;
        }

        private void ObserveFishInteractions()
        {
            if (!NetworkDriver.ObserveFishInteractions.Value || !NetworkDriver.TransmitFishObservations.Value ||
                _peers.Main.Snapshot.Role != SessionRole.Host) return;
            try
            {
                if (!_fishLifecycle.Healthy) throw new InvalidOperationException("Fish lifecycle observation unavailable.");
                _fishInteractions.Enable(ResolveHealthyFish);
                _fishInteractions.CheckHealthy(); _interactionWarning = null;
            }
            catch (Exception error)
            {
                string message = error.GetType().Name + ": " + error.Message;
                if (_interactionWarning != message) NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_INTERACTION_WARNING: " + message);
                _interactionWarning = message; _fishInteractions.Dispose();
            }
        }

        private HostEntityTarget? ResolveHealthyFish(long pointer)
        {
            if (!_fishLifecycle.Healthy) return null;
            HostEntityTarget? target = _fish.ResolveObservedPointer(pointer);
            return _fishLifecycle.Healthy ? target : null;
        }

        private void DrainFishInteractions()
        {
            int drained = 0;
            while (drained++ < 512 && _fishInteractions.TryTake(out FishInteractionEvent observed))
            {
                _interactionEvents++;
                if (observed.EntityId.HasValue) _mappedInteractionEvents++;
                bool nativeTargetAvailableAtDrain = false; float? hpAtDrain = null; string lookupError = null;
                if (observed.ResolvedEpoch.HasValue && observed.EntityId.HasValue)
                {
                    try
                    {
                        DR.AI.FishAISystem native = null;
                        nativeTargetAvailableAtDrain = _fishLifecycle.Healthy &&
                            _fish.TryResolveNativeFish(observed.ResolvedEpoch.Value, observed.EntityId.Value, out native);
                        if (nativeTargetAvailableAtDrain)
                        {
                            float hp = native.HP; if (float.IsFinite(hp)) hpAtDrain = hp;
                            HostEntityTarget? currentTarget = _fish.ResolveObservedPointer(native.Pointer.ToInt64());
                            if (_fishLifecycle.Healthy && currentTarget.HasValue &&
                                currentTarget.Value.SceneEpoch == observed.ResolvedEpoch && currentTarget.Value.EntityId == observed.EntityId &&
                                currentTarget.Value.Generation == observed.Generation) _nativeInteractionTargetsAtDrain++;
                            else { nativeTargetAvailableAtDrain = false; hpAtDrain = null; }
                        }
                    }
                    catch (Exception error) { _nativeInteractionLookupErrors++; lookupError = error.GetType().Name + ": " + error.Message; }
                }
                NetworkDriver.Logger.LogInfo("DAVECOOP_FISH_INTERACTION: " + JsonSerializer.Serialize(new
                {
                    observed.ProcessSequence, observed.CallId, ObservedKind = observed.ObservedKind.ToString(), OriginalMethodCode = observed.OriginalMethodCode.ToString(),
                    Stage = observed.Stage.ToString(), observed.CallbackThreadId, observed.ResolvedEpoch, observed.EntityId,
                    observed.Generation, observed.DataTid, observed.DamageReturnObserved, observed.CorrelationMatched,
                    NativeTargetAvailableAtDrain = nativeTargetAvailableAtDrain, HpAtDrain = hpAtDrain, NativeLookupError = lookupError,
                    ObservationOnly = true, NativeOutcomeConfirmed = false
                }));
            }
        }

        private void ObserveWorld(SessionSnapshot state)
        {
            if (!HostFishObservationsEnabled(state) || Time.unscaledTime < _nextWorldCapture) return;
            _nextWorldCapture = Time.unscaledTime + 0.2f;
            try
            {
                _fishLifecycle.Enable();
                if (!_fishLifecycle.Healthy) throw new InvalidOperationException("Fish lifecycle callback failed; state publication stopped.");
                WorldSnapshot snapshot = _fish.Capture(_local.Player.gameObject.scene, state.SceneEpoch, state.SceneKey, _peers.Main.Now, _catalog, _spines, _fishLifecycle.Tracker);
                if (!_fishLifecycle.Healthy) throw new InvalidOperationException("Fish lifecycle failed during state sampling.");
                if (_peers.Main.PublishWorld(snapshot)) _lastPublishedWorldRevision++;
                _worldWarning = null;
            }
            catch (Exception error)
            {
                _fish.Clear();
                string message = error.GetType().Name + ": " + error.Message;
                if (message != _worldWarning) NetworkDriver.Logger.LogWarning("DAVECOOP_WORLD_CAPTURE_WARNING: " + message);
                _worldWarning = message;
            }
        }

        private static bool HostFishObservationsEnabled(SessionSnapshot state) => state != null &&
            state.Role == SessionRole.Host && state.Phase != SessionPhase.Closed &&
            (NetworkDriver.TransmitFishObservations.Value || state.RemoteRequestsHostFishDisplay);

        private bool FishWorldDisplayEnabled(SessionSnapshot state) =>
            (NativeGuestInitializationController.Current != null || NetworkDriver.ShowFishWorld.Value) && FishDisplaySourceCurrent(state);

        private bool FishPreviewDisplayEnabled(SessionSnapshot state) =>
            NetworkDriver.ShowFishPreview != null && NetworkDriver.ShowFishPreview.Value && FishDisplaySourceCurrent(state);

        private bool FishDisplaySourceCurrent(SessionSnapshot state)
        {
            var startup = NativeGuestInitializationController.Current;
            if (startup == null) return true;
            // The Hello flag only asks the host for numbers. Rendering in a
            // temporary guest also needs the actual current quarantine source.
            if (_peers == null || _peers.Loopback != null || state == null ||
                state.Role != SessionRole.Guest || state.Phase != SessionPhase.Ready) return false;
            SessionSnapshot current = _peers.Main.Snapshot;
            return current.Role == SessionRole.Guest && current.Phase == SessionPhase.Ready &&
                current.RoomId == state.RoomId && current.SceneEpoch == state.SceneEpoch && current.SceneKey == state.SceneKey &&
                _local.IsAvailable && startup.CanDisplayHostFish(_peers.Main, _local.Player.gameObject.scene.handle);
        }

        public void Draw()
        {
            try
            {
                if (FishPreviewDisplayEnabled(_peers?.Main.Snapshot)) _fishPreview.DrawMarker();
                else _fishPreview.Clear("PreviewDisplayUnavailable");
            }
            catch (Exception error)
            {
                string message = error.GetType().Name + ": " + error.Message;
                if (_fishPreviewWarning != message) NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_PREVIEW_WARNING: " + message);
                _fishPreviewWarning = message; _fishPreview.Clear("Exception");
            }
            if (NetworkDriver.ShowPanel == null || !NetworkDriver.ShowPanel.Value) return;
            Matrix4x4 originalMatrix = GUI.matrix;
            bool originalEnabled = GUI.enabled;
            float scale = Math.Min(1f, Math.Min(Screen.width / 664f, Screen.height / 749f));
            try
            {
                if (scale > 0 && scale < 1) GUI.matrix = originalMatrix * Matrix4x4.Scale(new Vector3(scale, scale, 1));
                DrawPanel();
            }
            finally { GUI.matrix = originalMatrix; GUI.enabled = originalEnabled; }
        }

        private void DrawPanel()
        {
            GUI.Box(new Rect(12, 170, 640, 567), "MultiDave LAN prototype — F11");
            GUI.Label(new Rect(24, 194, 616, 28), "Player/fish display tests. Cooperative capture is still in development.");
            bool idle = _pending == null && _peers == null;
            bool originalEnabled = GUI.enabled;
            GUI.enabled = originalEnabled && idle;
            GUI.Label(new Rect(24, 226, 72, 24), "Host IPv4:");
            string address = GUI.TextField(new Rect(98, 226, 176, 25), NetworkDriver.HostAddress.Value, 64);
            if (address != NetworkDriver.HostAddress.Value) NetworkDriver.HostAddress.Value = address;
            GUI.Label(new Rect(286, 226, 38, 24), "Port:");
            _portText = GUI.TextField(new Rect(327, 226, 76, 25), _portText ?? "27182", 5);
            GUI.Label(new Rect(418, 226, 47, 24), "Name:");
            string name = GUI.TextField(new Rect(468, 226, 168, 25), NetworkDriver.PlayerName.Value, 32);
            if (name != NetworkDriver.PlayerName.Value) NetworkDriver.PlayerName.Value = name;
            if (GUI.Button(new Rect(24, 264, 140, 30), "Host room")) Start("host");
            if (GUI.Button(new Rect(176, 264, 140, 30), "Join room")) Start("guest");
            if (GUI.Button(new Rect(328, 264, 140, 30), "Local test")) Start("loopback");
            GUI.enabled = originalEnabled && !idle;
            if (GUI.Button(new Rect(480, 264, 156, 30), "Disconnect")) Disconnect();
            GUI.enabled = originalEnabled;
            GUI.Label(new Rect(24, 304, 612, 28), NetworkDriver.Status);
            bool observe = GUI.Toggle(new Rect(24, 336, 612, 25), NetworkDriver.TransmitFishObservations.Value, "Transmit read-only fish observations (diagnostic)");
            if (observe != NetworkDriver.TransmitFishObservations.Value) NetworkDriver.TransmitFishObservations.Value = observe;
            bool preview = GUI.Toggle(new Rect(24, 367, 612, 25), NetworkDriver.ShowFishPreview.Value, "Preview one received fish (display only)");
            if (preview != NetworkDriver.ShowFishPreview.Value) NetworkDriver.ShowFishPreview.Value = preview;
            if (GUI.Button(new Rect(24, 399, 220, 26), "Select nearest preview fish")) _fishPreview.RequestReselect();
            GUI.Label(new Rect(256, 399, 380, 26), "Preview fish cannot be caught yet.");
            bool worldDisplay = GUI.Toggle(new Rect(24, 433, 612, 25), NetworkDriver.ShowFishWorld.Value, "Display received fish roster (display only)");
            if (worldDisplay != NetworkDriver.ShowFishWorld.Value) NetworkDriver.ShowFishWorld.Value = worldDisplay;
            bool interaction = GUI.Toggle(new Rect(24, 464, 612, 25), NetworkDriver.ObserveFishInteractions.Value, "Observe host harpoon and fish interactions (read-only)");
            if (interaction != NetworkDriver.ObserveFishInteractions.Value) NetworkDriver.ObserveFishInteractions.Value = interaction;
            bool mapCalls = GUI.Toggle(new Rect(24, 495, 612, 25), NetworkDriver.ObserveMapSelectionCalls.Value, "Observe map selection calls (read-only)");
            if (mapCalls != NetworkDriver.ObserveMapSelectionCalls.Value) NetworkDriver.ObserveMapSelectionCalls.Value = mapCalls;
            bool lootCalls = GUI.Toggle(new Rect(24, 526, 612, 25), NetworkDriver.ObserveLootCalls.Value, "Observe loot and return calls (read-only)");
            if (lootCalls != NetworkDriver.ObserveLootCalls.Value) NetworkDriver.ObserveLootCalls.Value = lootCalls;
            bool origins = GUI.Toggle(new Rect(24, 557, 612, 25), NetworkDriver.ObserveMapOrigins.Value, "Observe loading coroutine and scene ownership (read-only)");
            if (origins != NetworkDriver.ObserveMapOrigins.Value) NetworkDriver.ObserveMapOrigins.Value = origins;
            SessionPeer guest = _peers?.Loopback ?? (_peers?.Main.Snapshot.Role == SessionRole.Guest ? _peers.Main : null);
            GUI.enabled = originalEnabled && guest != null && guest.Snapshot.Phase == SessionPhase.Ready && _fishPreview.SelectedEntity > 0;
            if (GUI.Button(new Rect(24, 590, 220, 26), "Check selected fish target")) _fishActions.SubmitProbe(guest, _fishPreview.SelectedEntity);
            GUI.enabled = originalEnabled;
            GUI.Label(new Rect(256, 590, 380, 26), _fishActions.Status);
            GUI.Label(new Rect(24, 625, 612, 100), _message + "\n" + _mapChoices.Status + "\n" + (_lootObserver?.Status ?? "Loot observer: off") + "\n" + (_mapOrigins?.Status ?? "Map origin observer: off"));
        }

        private void Start(string mode)
        {
            if (_pending != null || _peers != null) return;
            var startupGuest = NativeGuestInitializationController.Current;
            if (startupGuest != null && (mode != "guest" || startupGuest.Failed))
            { _message = "This temporary-progress process can only join its initial guest room. Restart to change mode."; return; }
            try
            {
                if (!int.TryParse(_portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
                    throw new ArgumentException("Port must be 1..65535.");
                if (mode == "guest" && (!IPAddress.TryParse(NetworkDriver.HostAddress.Value, out IPAddress host) || host.AddressFamily != AddressFamily.InterNetwork))
                    throw new ArgumentException("Enter the host's LAN IPv4 address.");
                PeerIdentity identity = Identity(mode == "guest" && startupGuest != null);
                NetworkDriver.Port.Value = port;
                string address = NetworkDriver.HostAddress.Value;
                _attempt = new CancellationTokenSource(); CancellationToken token = _attempt.Token;
                // All values above were copied from Unity/config on the main thread.
                // Socket I/O is asynchronous; continuations below never touch Unity.
                _pending = ConnectAsync(mode, address, port, identity, token);
                NetworkDriver.Status = mode == "host" ? "Network: listening on port " + port : "Network: connecting";
                _message = mode == "host" ? "Guest: enter this computer's LAN IPv4 and the same port." : "Waiting for version handshake.";
            }
            catch (Exception error) { Fail(error); }
        }

        private static async Task<Peers> ConnectAsync(string mode, string address, int port, PeerIdentity identity, CancellationToken token)
        {
            if (mode == "guest") return new Peers { Main = await LanGuest.ConnectAsync(address, port, identity, token).ConfigureAwait(false) };
            using var listener = new LanHost(mode == "host" ? IPAddress.Any : IPAddress.Loopback, mode == "host" ? port : 0);
            Task<SessionPeer> accepted = listener.AcceptOneAsync(identity, token);
            if (mode == "host") return new Peers { Main = await accepted.ConfigureAwait(false) };
            SessionPeer guest = null;
            try
            {
                guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, identity, token).ConfigureAwait(false);
                return new Peers { Main = await accepted.ConfigureAwait(false), Loopback = guest };
            }
            catch
            {
                guest?.Dispose(); listener.Dispose();
                try { (await accepted.ConfigureAwait(false)).Dispose(); } catch { }
                throw;
            }
        }

        private void FinishAttempt()
        {
            if (_pending == null || !_pending.IsCompleted) return;
            Task<Peers> finished = _pending; _pending = null;
            _peers = finished.GetAwaiter().GetResult();
            _fishActions.BindRoom(_peers.Main);
            _cargoInventory.BindRoom(_peers.Main); _nextCargoObservation = 0;
            if (NativeGuestInitializationController.Current == null && _peers.Main.Snapshot.Role == SessionRole.Host)
                _mapOrigins.Update(true, Time.unscaledTime);
            _mapChoices.BindRoom(_peers.Main, _mapSelectionHooks.ProcessAccepted, _mapOrigins.RunId, _mapOrigins.ActiveOwnerLife);
            var startupGuest = NativeGuestInitializationController.Current;
            if (startupGuest != null && !startupGuest.BindPeer(_peers.Main))
                throw new InvalidOperationException("Guest startup room binding was rejected.");
            if (startupGuest != null && !startupGuest.MapController.BindTransport(_peers.Main, _mapChoices))
                throw new InvalidOperationException("Guest map transport binding was rejected.");
            RemotePreview.NetworkActive = true;
            _nextOwnerCheck = 0; _nextCapture = 0; _lastError = null;
            NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_CONNECTED: " + (_peers.Loopback == null ? _peers.Main.Snapshot.Role.ToString() : "local TCP diagnostic; one game process"));
        }

        private static PeerIdentity Identity(bool requestsHostFishDisplay)
        {
            string root = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "..", ".."));
            string manifest = Path.Combine(root, "appmanifest_1868140.acf");
            if (!File.Exists(manifest)) throw new InvalidOperationException("Steam build manifest not found.");
            Match match = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"([0-9]+)\"");
            if (!match.Success) throw new InvalidOperationException("Steam build identity not found.");
            if (match.Groups[1].Value != "25315876" || Application.unityVersion != "6000.0.52f1")
                throw new InvalidOperationException("This game build has not been verified for the movement prototype.");
            string name = NetworkDriver.PlayerName.Value.Trim();
            return new PeerIdentity
            {
                ModVersion = Plugin.Version, SteamBuildId = match.Groups[1].Value, UnityVersion = Application.unityVersion,
                Name = name.Length == 0 ? "Dave" : name, RequestsHostFishDisplay = requestsHostFishDisplay
            };
        }

        private void Fail(Exception error)
        {
            string reason = error.Message; Disconnect();
            _message = reason; NetworkDriver.Status = "Network: error";
            NetworkDriver.Logger.LogWarning("DAVECOOP_NETWORK_WARNING: " + error.GetType().Name + ": " + reason);
        }

        private void Disconnect()
        {
            _hostFishInterest?.Clear("Disconnected");
            NativeGuestInitializationController.Current?.NetworkDisconnected();
            if (NetworkDriver.ObserveMapOrigins != null) NetworkDriver.ObserveMapOrigins.Value = false;
            _mapOrigins?.Stop();
            if (NetworkDriver.ObserveLootCalls != null) NetworkDriver.ObserveLootCalls.Value = false;
            _lootObserver?.Stop();
            if (_mapCalls != null || _mapSelectionHooks.Installed)
            {
                NetworkDriver.ObserveMapSelectionCalls.Value = false;
                StopMapSelectionCalls();
            }
            bool hadSession = _peers != null || _pending != null;
            _attempt?.Cancel();
            Task<Peers> pending = _pending; _pending = null;
            if (pending != null) _ = DisposePendingAsync(pending);
            _peers?.Dispose(); _peers = null;
            _cargoInventory.Disconnect();
            _attempt?.Dispose(); _attempt = null;
            _scene = null; _layoutMessage = null; _local.Clear(); _catalog.Clear(); _display.Clear(); _displayEpoch = 0;
            _fish.ResetRoom(); _fishLifecycle.Dispose(); _worldEpoch = 0; _lastRemoteWorldRevision = 0; _lastPublishedWorldRevision = 0; _lastRemoteFishCount = 0; _worldWarning = null;
            _fishPreview.Clear("Disconnected"); _spines.Clear(); _fishPreviewWarning = null;
            _fishWorld.Clear("Disconnected"); _fishWorldWarning = null; _fishInteractions.Dispose();
            _mapSelection?.Clear(); _lastMapSelection = null; _mapSelectionStatus = null;
            _fishActions.Clear();
            _mapChoices.Clear(_mapSelectionHooks.ProcessAccepted);
            RemotePreview.NetworkActive = false;
            NetworkDriver.Status = "Network: offline (F11)"; _message = "Disconnected.";
            if (hadSession) NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_DISCONNECTED: peers disposed; own avatar/fish display cleared; local replay restored.");
        }

        private static async Task DisposePendingAsync(Task<Peers> pending)
        {
            try { (await pending.ConfigureAwait(false)).Dispose(); } catch { }
        }

        private void RestoreCursor()
        {
            if (!_panelActive) return;
            Cursor.visible = _savedCursorVisible; Cursor.lockState = _savedCursorLock; _panelActive = false;
        }

        private void UpdateHostFishAreas()
        {
            if (_hostFishInterest == null || _hostFishAreasFailed) return;
            try
            {
                _hostFishInterest.TryRead(out _);
                if (_hostFishInterest.Failed)
                    throw new InvalidOperationException("Host fish interest source became unavailable.");
                _hostFishAllocatorArea.CheckHealthy();
                _hostFishLodArea.Update();
                _hostFishVisibilityArea.CheckHealthy();
                if (!_hostFishAllocatorArea.Healthy || !_hostFishLodArea.Healthy || !_hostFishVisibilityArea.Healthy)
                    StopHostFishAreas(new InvalidOperationException("Host fish area adapter became unavailable."));
            }
            catch (Exception error) { StopHostFishAreas(error); }
        }

        private void StopHostFishAreas(Exception error)
        {
            _hostFishAreasFailed = true;
            _hostFishInterest?.Clear("AdapterFailed");
            try { NetworkDriver.Logger.LogWarning("DAVECOOP_HOST_FISH_AREAS_UNAVAILABLE: " + error.GetType().Name + ": " + error.Message); }
            catch { /* Area diagnostics must not interrupt the movement session. */ }
        }

        public void Dispose()
        {
            StopMapSelectionCalls(); Disconnect(); RestoreCursor();
            _hostFishAllocatorArea?.Dispose(); _hostFishLodArea?.Dispose(); _hostFishVisibilityArea?.Dispose();
        }
    }
}
