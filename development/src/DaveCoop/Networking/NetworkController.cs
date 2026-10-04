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
        private long _worldEpoch;
        private long _lastRemoteWorldRevision;
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
                    NetworkDriver.Logger.LogInfo("DAVECOOP_NETWORK_READY: F11 opens LAN movement test panel; no automatic connection.");
                }
                if (Input.GetKeyDown(KeyCode.F11)) NetworkDriver.ShowPanel.Value = !NetworkDriver.ShowPanel.Value;
                if (NetworkDriver.ShowPanel.Value && !_panelActive)
                {
                    _savedCursorVisible = Cursor.visible; _savedCursorLock = Cursor.lockState; _panelActive = true;
                }
                if (!NetworkDriver.ShowPanel.Value) RestoreCursor();
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
                if (state.Phase != SessionPhase.Ready || state.SceneEpoch != _worldEpoch)
                {
                    _fish.Clear(); _lastRemoteWorldRevision = 0; _lastRemoteFishCount = 0;
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
                        UnkeyedLocalParts = _local.UnkeyedVisibleParts, RemoteVisibleParts = _display.VisibleParts,
                        RemoteUnknownAssets = _display.UnknownAssets, RemoteHistoryCount = _display.HistoryCount,
                        ObservedHostFish = _fish.ObservedFish, _fish.UninitializedFish,
                        RemoteWorldRevision = _lastRemoteWorldRevision, RemoteFishCount = _lastRemoteFishCount
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
                if (Time.unscaledTime >= _nextCapture)
                {
                    _nextCapture = Time.unscaledTime + 1f / 30;
                    PlayerFrame frame = _local.Capture(_peers.Main.Now, _catalog);
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
            _fish.Clear(); _worldEpoch = 0; _lastRemoteWorldRevision = 0; _lastRemoteFishCount = 0;
        }

        private void ObserveWorld(SessionSnapshot state)
        {
            if (state.Role != SessionRole.Host || !NetworkDriver.TransmitFishObservations.Value || Time.unscaledTime < _nextWorldCapture) return;
            _nextWorldCapture = Time.unscaledTime + 0.2f;
            try
            {
                WorldSnapshot snapshot = _fish.Capture(_local.Player.gameObject.scene, state.SceneEpoch, state.SceneKey, _peers.Main.Now);
                _peers.Main.PublishWorld(snapshot); _worldWarning = null;
            }
            catch (Exception error)
            {
                string message = error.GetType().Name + ": " + error.Message;
                if (message != _worldWarning) NetworkDriver.Logger.LogWarning("DAVECOOP_WORLD_CAPTURE_WARNING: " + message);
                _worldWarning = message;
            }
        }

        public void Draw()
        {
            if (NetworkDriver.ShowPanel == null || !NetworkDriver.ShowPanel.Value) return;
            GUI.Box(new Rect(12, 170, 640, 280), "MultiDave LAN movement test — F11");
            GUI.Label(new Rect(24, 194, 616, 28), "Movement display only. Fish, items and results are not synchronized.");
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
            GUI.Label(new Rect(24, 369, 612, 65), _message);
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
            _attempt?.Cancel();
            Task<Peers> pending = _pending; _pending = null;
            if (pending != null) _ = DisposePendingAsync(pending);
            _peers?.Dispose(); _peers = null;
            _attempt?.Dispose(); _attempt = null;
            _scene = null; _layoutMessage = null; _local.Clear(); _catalog.Clear(); _display.Clear(); _displayEpoch = 0;
            _fish.Clear(); _worldEpoch = 0; _lastRemoteWorldRevision = 0; _lastRemoteFishCount = 0; _worldWarning = null;
            RemotePreview.NetworkActive = false;
            NetworkDriver.Status = "Network: offline (F11)"; _message = "Disconnected.";
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
