using System;
using System.Collections.Generic;
using System.Threading;
using DaveCoop.Core.World;
using UnityEngine;

namespace DaveCoop.Networking
{
    // Native wrappers live only during Capture. Queued observations contain
    // copied CLR values, and are never a map-adoption permission.
    internal sealed class MapSelectionCallObservation
    {
        public long ProcessSequence { get; set; }
        public string Stage { get; set; }
        public int CallbackThreadId { get; set; }
        public int? UnityFrame { get; set; }
        public bool MainThread { get; set; }
        public MapRouteSelection Route { get; set; }
        public string RouteFingerprint { get; set; }
        public string RouteUnavailableReason { get; set; }
        public string ControllerSceneName { get; set; }
        public string ControllerAddress { get; set; }
        public bool? SelectionPresent { get; set; }
        public bool? Addressable { get; set; }
        public string SelectedPrefabName { get; set; }
        public string PrefabObjectName { get; set; }
        public string SceneLoadKey { get; set; }
        public int? SceneLoadMode { get; set; }
        public bool? ActivateOnLoad { get; set; }
        public bool Truncated { get; set; }
        public string ReadError { get; set; }
        public bool ObservationOnly => true;
        public bool CrossMachineAddressVerified => false;
        public bool HostSelectionApplied => false;
        public bool ResourceLoadCompletionObserved => false;
    }

    internal sealed class MapSelectionHookCapture
    {
        private const int MaxQueued = 64;
        private readonly object _gate = new object();
        private readonly Queue<MapSelectionCallObservation> _pending = new Queue<MapSelectionCallObservation>(MaxQueued);
        private readonly int _unityThreadId;
        private readonly MapSelectionCapture _maps;
        private bool _accepting = true;
        private static long _processDropped;
        private static long _processUnexpectedThreads;
        private static long _processReadErrors;
        public long Dropped => Interlocked.Read(ref _processDropped);
        public long UnexpectedThreads => Interlocked.Read(ref _processUnexpectedThreads);
        public long ReadErrors => Interlocked.Read(ref _processReadErrors);

        public MapSelectionHookCapture(int unityThreadId)
        {
            if (unityThreadId < 1) throw new ArgumentException("Missing map observation ownership.");
            _unityThreadId = unityThreadId;
            // Share the extraction implementation, not the post-load reader's
            // two-frame correlation state or its unavailable reason.
            _maps = new MapSelectionCapture(unityThreadId);
        }

        public void Capture(MapSelectionCallback call)
        {
            lock (_gate)
            {
                if (!_accepting) return;
                if (_pending.Count >= MaxQueued) { Interlocked.Increment(ref _processDropped); return; }
            }
            var observation = new MapSelectionCallObservation
            {
                ProcessSequence = call.ProcessSequence, Stage = call.Stage.ToString(), CallbackThreadId = call.ManagedThreadId,
                MainThread = call.ManagedThreadId == _unityThreadId && Environment.CurrentManagedThreadId == _unityThreadId
            };
            if (!observation.MainThread)
            {
                // Do not read a Unity field, wrapper pointer or frame counter
                // from a callback whose native thread ownership is unknown.
                observation.ReadError = "Callback is outside the confirmed Unity thread.";
                Interlocked.Increment(ref _processUnexpectedThreads);
            }
            else
            {
                try
                {
                    observation.UnityFrame = Time.frameCount;
                    switch (call.Stage)
                    {
                        case MapSelectionStage.RouteCachedAfter:
                        case MapSelectionStage.RouteRestoredAfter:
                            ReadRoute(call.Context, observation);
                            break;
                        case MapSelectionStage.IgpSelectedAfter:
                            // Preserve the short-lived original choice before
                            // optional controller-address diagnostics can fail.
                            ReadInfo(call.SelectedInfo, observation);
                            if (call.Controller != null)
                            {
                                observation.ControllerSceneName = Text(call.Controller.gameObject.scene.name, MapSelections.MaxSceneName, observation);
                                observation.ControllerAddress = _maps.ReadControllerAddress(call.Controller);
                            }
                            // CurrIGPSetInfo may be assigned by the caller after
                            // this return. Preserve the actual original result.
                            break;
                        case MapSelectionStage.IgpPrefabFactoryBefore:
                            // This call provides no unique controller binding.
                            // An IEnumerator factory is not loading completion.
                            ReadInfo(call.LoadingInfo, observation);
                            break;
                        case MapSelectionStage.SceneLoadCallBefore:
                            observation.SceneLoadKey = Text(call.SceneKey, MapSelections.MaxPrefabName, observation);
                            observation.SceneLoadMode = (int)call.LoadMode;
                            observation.ActivateOnLoad = call.ActivateOnLoad;
                            ReadRoute(SceneContext._s_Instance_k__BackingField, observation);
                            break;
                    }
                }
                catch (Exception error)
                {
                    observation.ReadError = Text(error.GetType().Name + ": " + error.Message, 256, observation);
                    // A failed read can preserve scalar diagnostics but must
                    // not expose a half-copied route as a usable candidate.
                    observation.Route = null; observation.RouteFingerprint = null;
                    Interlocked.Increment(ref _processReadErrors);
                }
            }
            lock (_gate)
            {
                if (!_accepting) return;
                if (_pending.Count >= MaxQueued) Interlocked.Increment(ref _processDropped);
                else _pending.Enqueue(observation);
            }
        }

        private void ReadRoute(SceneContext context, MapSelectionCallObservation observation)
        {
            MapRouteSelection route = _maps.ReadRouteCandidate(context);
            observation.RouteUnavailableReason = _maps.UnavailableReason;
            if (route == null) return;
            observation.Route = MapSelections.CopyRoute(route);
            observation.RouteFingerprint = MapSelections.FingerprintRoute(observation.Route);
        }

        private static void ReadInfo(IGPSetInfo info, MapSelectionCallObservation observation)
        {
            observation.SelectionPresent = info != null;
            if (info == null) return;
            // These getters are generated direct field proxies, not original
            // random-selection, condition or save-interface method calls.
            observation.Addressable = info.isAddressableMode;
            observation.SelectedPrefabName = Text(info.prefabName, MapSelections.MaxPrefabName, observation);
            IGPSetObject prefab = info.Prefab;
            observation.PrefabObjectName = prefab == null ? null : Text(prefab.name, MapSelections.MaxPrefabName, observation);
        }

        private static string Text(string value, int maximum, MapSelectionCallObservation observation)
        {
            if (value == null || value.Length <= maximum) return value;
            observation.Truncated = true;
            int length = maximum;
            if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
            return value.Substring(0, length);
        }

        public bool TryTake(out MapSelectionCallObservation observation)
        {
            lock (_gate)
            {
                if (_pending.Count == 0) { observation = null; return false; }
                observation = _pending.Dequeue(); return true;
            }
        }

        public void Stop() { lock (_gate) { _accepting = false; _pending.Clear(); } }
    }
}
