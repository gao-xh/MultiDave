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
        private readonly FishActionController _fishActions = new FishActionController();
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
                    NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_READY: F11 opens LAN movement test panel; no automatic connection.");
                }
                if (Input.GetKeyDown(KeyCode.F11)) NetworkDriver.ShowPanel.Value = !NetworkDriver.ShowPanel.Value;
                if (NetworkDriver.ShowPanel.Value && !_panelActive)
                {
                    _savedCursorVisible = Cursor.visible; _savedCursorLock = Cursor.lockState; _panelActive = true;
                }
                if (!NetworkDriver.ShowPanel.Value) RestoreCursor();
                ObserveRouteInputs();
                FinishAttempt();
                if (_peers == null) return;
                SessionSnapshot state = _peers.Main.Snapshot;
                while (_peers.Main.TryTakeEvent(out SessionEvent item))
                    NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_EVENT: " + JsonSerializer.Serialize(item));
                if (state.Phase == SessionPhase.Closed)
                {
                    string reason = state.Reason; Disconnect(); _message = reason; NetworkDriver.Status = "Network: " + reason; return;
                }
                UpdateLocalScene();
                state = _peers.Main.Snapshot;
                if (state.Role != SessionRole.Host || !NetworkDriver.TransmitFishObservations.Value) _fishLifecycle.Dispose();
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
                if (_peers.Main.TryTakeRemoteFrame(out ReceivedFrame received) && state.Phase == SessionPhase.Ready &&
                    received.Frame.SceneEpoch == state.SceneEpoch) _display.Receive(received);
                // The internal peer is also pure CLR. Drain its mailbox so the
                // diagnostic exercises both directions without touching Unity there.
                if (_peers.Loopback != null) _peers.Loopback.TryTakeRemoteFrame(out _);
                SessionPeer worldReceiver = _peers.Loopback ?? _peers.Main;
                if (worldReceiver.TryTakeRemoteWorld(out WorldSnapshot world) && world.SceneEpoch == state.SceneEpoch && state.Phase == SessionPhase.Ready)
                {
                    _lastRemoteWorldRevision = world.Revision; _lastRemoteFishCount = world.Entities.Length;
                    if (NetworkDriver.ShowFishPreview.Value) _fishPreview.Receive(world, worldReceiver.Now, _local, _peers.Loopback != null);
                    if (NetworkDriver.ShowFishWorld.Value) _fishWorld.Receive(world, worldReceiver.Now);
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
                        state.RoundTripSeconds, LocalPlayer = _local.PlayerId, LocalParts = _local.PartCount,
                        UnkeyedLocalParts = _local.UnkeyedVisibleParts, SkippedDestroyedLocalParts = _local.SkippedDestroyedParts, RemoteVisibleParts = _display.VisibleParts,
                        RemoteUnknownAssets = _display.UnknownAssets, RemoteHistoryCount = _display.HistoryCount,
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
                if (NetworkDriver.ShowFishPreview.Value)
                {
                    try
                    {
                        _fishPreview.Render((_peers.Loopback ?? _peers.Main).Now, Math.Clamp(delay, 0.05f, 0.5f), _local, _catalog, _spines, _peers.Loopback != null);
                        _fishPreviewWarning = null;
                    }
                    catch (Exception error)
                    {
                        string message = error.GetType().Name + ": " + error.Message;
                        if (message != _fishPreviewWarning) NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_PREVIEW_WARNING: " + message);
                        _fishPreviewWarning = message; _fishPreview.Clear("Exception");
                    }
                }
                else _fishPreview.Clear("PreviewDisabled");
                _fishPreview.Trace();
                if (NetworkDriver.ShowFishWorld.Value)
                {
                    try
                    {
                        _fishWorld.Render((_peers.Loopback ?? _peers.Main).Now, Math.Clamp(delay, 0.05f, 0.5f), _local, _catalog, _spines, _peers.Loopback != null);
                        _fishWorldWarning = null;
                    }
                    catch (Exception error)
                    {
                        string message = error.GetType().Name + ": " + error.Message;
                        if (message != _fishWorldWarning) NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_WORLD_WARNING: " + message);
                        _fishWorldWarning = message; _fishWorld.Clear("Exception");
                    }
                }
                else _fishWorld.Clear("WorldDisplayDisabled");
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
            if (state.Role != SessionRole.Host || !NetworkDriver.TransmitFishObservations.Value || Time.unscaledTime < _nextWorldCapture) return;
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

        public void Draw()
        {
            if (NetworkDriver.ShowFishPreview != null && NetworkDriver.ShowFishPreview.Value)
            {
                try { _fishPreview.DrawMarker(); }
                catch (Exception error)
                {
                    string message = error.GetType().Name + ": " + error.Message;
                    if (_fishPreviewWarning != message) NetworkDriver.Logger.LogWarning("DAVECOOP_FISH_PREVIEW_WARNING: " + message);
                    _fishPreviewWarning = message;
                }
            }
            if (NetworkDriver.ShowPanel == null || !NetworkDriver.ShowPanel.Value) return;
            GUI.Box(new Rect(12, 170, 640, 452), "MultiDave LAN prototype — F11");
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
            SessionPeer guest = _peers?.Loopback ?? (_peers?.Main.Snapshot.Role == SessionRole.Guest ? _peers.Main : null);
            GUI.enabled = originalEnabled && guest != null && guest.Snapshot.Phase == SessionPhase.Ready && _fishPreview.SelectedEntity > 0;
            if (GUI.Button(new Rect(24, 498, 220, 26), "Check selected fish target")) _fishActions.SubmitProbe(guest, _fishPreview.SelectedEntity);
            GUI.enabled = originalEnabled;
            GUI.Label(new Rect(256, 498, 380, 26), _fishActions.Status);
            GUI.Label(new Rect(24, 532, 612, 65), _message);
        }

        private void Start(string mode)
        {
            if (_pending != null || _peers != null) return;
            try
            {
                if (!int.TryParse(_portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
                    throw new ArgumentException("Port must be 1..65535.");
                if (mode == "guest" && (!IPAddress.TryParse(NetworkDriver.HostAddress.Value, out IPAddress host) || host.AddressFamily != AddressFamily.InterNetwork))
                    throw new ArgumentException("Enter the host's LAN IPv4 address.");
                PeerIdentity identity = Identity();
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
            RemotePreview.NetworkActive = true;
            _nextOwnerCheck = 0; _nextCapture = 0; _lastError = null;
            NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_CONNECTED: " + (_peers.Loopback == null ? _peers.Main.Snapshot.Role.ToString() : "local TCP diagnostic; one game process"));
        }

        private static PeerIdentity Identity()
        {
            string root = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "..", ".."));
            string manifest = Path.Combine(root, "appmanifest_1868140.acf");
            if (!File.Exists(manifest)) throw new InvalidOperationException("Steam build manifest not found.");
            Match match = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"([0-9]+)\"");
            if (!match.Success) throw new InvalidOperationException("Steam build identity not found.");
            if (match.Groups[1].Value != "25315876" || Application.unityVersion != "6000.0.52f1")
                throw new InvalidOperationException("This game build has not been verified for the movement prototype.");
            string name = NetworkDriver.PlayerName.Value.Trim();
            return new PeerIdentity { ModVersion = Plugin.Version, SteamBuildId = match.Groups[1].Value, UnityVersion = Application.unityVersion, Name = name.Length == 0 ? "Dave" : name };
        }

        private void Fail(Exception error)
        {
            string reason = error.Message; Disconnect();
            _message = reason; NetworkDriver.Status = "Network: error";
            NetworkDriver.Logger.LogWarning("DAVECOOP_NETWORK_WARNING: " + error.GetType().Name + ": " + reason);
        }

        private void Disconnect()
        {
            bool hadSession = _peers != null || _pending != null;
            _attempt?.Cancel();
            Task<Peers> pending = _pending; _pending = null;
            if (pending != null) _ = DisposePendingAsync(pending);
            _peers?.Dispose(); _peers = null;
            _attempt?.Dispose(); _attempt = null;
            _scene = null; _layoutMessage = null; _local.Clear(); _catalog.Clear(); _display.Clear(); _displayEpoch = 0;
            _fish.ResetRoom(); _fishLifecycle.Dispose(); _worldEpoch = 0; _lastRemoteWorldRevision = 0; _lastPublishedWorldRevision = 0; _lastRemoteFishCount = 0; _worldWarning = null;
            _fishPreview.Clear("Disconnected"); _spines.Clear(); _fishPreviewWarning = null;
            _fishWorld.Clear("Disconnected"); _fishWorldWarning = null; _fishInteractions.Dispose();
            _mapSelection?.Clear(); _lastMapSelection = null; _mapSelectionStatus = null;
            _fishActions.Clear();
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

        public void Dispose() { Disconnect(); RestoreCursor(); }
    }
}
