using System;
using DaveCoop.Core;
using System.Collections.Generic;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaveCoop.Core.Actions;
using DaveCoop.Core.Protocol;
using DaveCoop.Core.Session;
using DaveCoop.Core.Transport;
using DaveCoop.Core.World;

internal static class MapChoiceTransportTests
{
    internal static void WirePayloadAndProtocolSeven()
    {
        Assert(new PeerIdentity().ProtocolVersion == 9, "crew actor negotiation did not advance the protocol");
        var pair = new Pair();
        MapRouteSelection route = Route(17);
        MapRouteSlice slice = MapChoiceFrames.SplitRoute(route, 1)[0];
        var packets = new[]
        {
            Packet(pair.Room, slice),
            new WirePacket { Kind = PacketKind.MapIgpChoice, Sequence = 1, RoomId = pair.Room, MapChoice = Choice(1, 1, MapSelections.FingerprintRoute(route)) },
            new WirePacket { Kind = PacketKind.MapChoiceRetire, Sequence = 1, RoomId = pair.Room, MapRetire = new MapChoiceRetire { Generation = 1, Reason = "source invalidated" } }
        };
        foreach (WirePacket packet in packets)
        {
            byte[] encoded = PacketCodec.Encode(packet);
            Assert(encoded.Length < PacketCodec.MaxPacketBytes && PacketCodec.Decode(encoded).Kind == packet.Kind, "map payload did not round trip within the wire bound");
            packet.Reason = "extra payload";
            Throws<ProtocolException>(() => PacketCodec.Encode(packet));
            packet.Reason = null;
        }
        packets[0].MapRoute.Scenes[0].SceneId = 0;
        Throws<ProtocolException>(() => PacketCodec.Encode(packets[0]));
        packets[1].Kind = PacketKind.MapRouteSlice;
        Throws<ProtocolException>(() => PacketCodec.Encode(packets[1]));
    }

    internal static void PreloadRouteAtomicRoundTrip()
    {
        var pair = new Pair();
        MapRouteSelection route = Route(17);
        Assert(pair.Host.PublishMapRoute(route, 0.1), "WaitingForScene host rejected preload evidence");
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        route.Scenes[0].SceneName = "caller mutation";
        for (int i = 0; i < 3; i++)
        {
            Assert(pair.Host.TryTakePacket(out WirePacket packet) && packet.Kind == PacketKind.MapRouteSlice && packet.MapRoute.Index == i,
                "route slices were overwritten or reordered");
            packet.Sequence = i + 1; pair.Guest.Receive(packet, 0.1);
            if (i == 0)
            {
                Assert(pair.Guest.TryTakeRemoteMapChoices(out MapChoiceSnapshot partial) && partial.Generation == 1 && partial.Route == null,
                    "first slice did not revoke the prior usable candidate");
            }
            else if (i == 1) Assert(!pair.Guest.TryTakeRemoteMapChoices(out _), "partial batch exposed a route");
        }
        Assert(pair.Guest.TryTakeRemoteMapChoices(out MapChoiceSnapshot complete) && complete.Route.Scenes.Length == 17 &&
            complete.Route.Scenes[0].SceneName == "dive-0" && complete.RouteFingerprint == fingerprint && complete.Choices.Length == 0,
            "atomic route commit or publisher ownership was lost");
        complete.Route.Scenes[0].SceneName = "receiver mutation";
        var choice = Choice(1, 1, fingerprint);
        pair.Host.PublishMapIgpChoice(choice, 0.2); choice.SelectedPrefabName = "caller mutation"; pair.Pump(0.2);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out complete) && complete.Route.Scenes[0].SceneName == "dive-0" &&
            complete.Choices[0].SelectedPrefabName == "IGP_A" && complete.LastChoiceRevision == 1 &&
            complete.ObservationOnly && !complete.HostSelectionApplied, "choice copying became adoption permission or aliased DTOs");
        Assert(pair.Host.Snapshot.Phase == SessionPhase.WaitingForScene && pair.Guest.Snapshot.Phase == SessionPhase.WaitingForScene &&
            pair.Host.Snapshot.SceneEpoch == 0 && pair.Guest.Snapshot.SceneEpoch == 0, "map evidence elevated scene readiness");
    }

    internal static void ReplacedBatchRevokesPartial()
    {
        var pair = new Pair();
        pair.Host.PublishMapRoute(Route(17), 0.1);
        Assert(pair.Host.TryTakePacket(out WirePacket first), "fixture did not start old route batch");
        first.Sequence = 1; pair.Guest.Receive(first, 0.1); pair.Guest.TryTakeRemoteMapChoices(out _);
        string oldFingerprint = first.MapRoute.RouteFingerprint;
        pair.Host.PublishMapRoute(Route(3), 0.2); pair.Pump(0.2);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out MapChoiceSnapshot current) && current.Generation == 2 && current.Route.Scenes.Length == 3,
            "new route did not cancel the started old batch");
        Assert(!pair.Host.TryTakePacket(out _), "old unsent route slices survived replacement");
        pair.Guest.Receive(first, 0.3);
        pair.Guest.Receive(new WirePacket { Kind = PacketKind.MapIgpChoice, RoomId = pair.Room, Sequence = 2,
            MapChoice = Choice(1, 1, oldFingerprint) }, 0.3);
        Assert(!pair.Guest.TryTakeRemoteMapChoices(out _) && pair.Guest.Snapshot.MapChoiceGeneration == 2,
            "old generation revived a partial route or choice");
        pair.Host.RetireMapChoices("new source", 0.4);
        pair.Host.PublishMapRoute(Route(4), 0.4);
        Assert(pair.Host.TryTakePacket(out WirePacket retire) && retire.Kind == PacketKind.MapChoiceRetire && retire.MapRetire.Generation == 2,
            "retirement did not precede the next generation");
        retire.Sequence = 3; pair.Guest.Receive(retire, 0.4);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out current) && current.Retired && current.Route == null, "retirement left an old usable route");
        pair.Pump(0.4);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out current) && current.Generation == 3 && !current.Retired && current.Route.Scenes.Length == 4,
            "retire/new-generation ordering canceled the fresh route");
    }

    internal static void ChoiceFifoBoundsAndRetirement()
    {
        var pair = new Pair(); pair.Host.PublishMapRoute(Route(3), 0.1); pair.Pump(0.1);
        pair.Guest.TryTakeRemoteMapChoices(out _);
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        for (int i = 1; i <= SessionMachine.MaxOutgoingMapPackets; i++)
            Assert(pair.Host.PublishMapIgpChoice(Choice(1, i, fingerprint), 0.2), "bounded FIFO rejected within capacity");
        Assert(!pair.Host.PublishMapIgpChoice(Choice(1, SessionMachine.MaxOutgoingMapPackets + 1, fingerprint), 0.2),
            "overflow silently kept a partially published generation");
        Assert(pair.Host.Snapshot.MapChoiceGeneration == 1 && pair.Host.Snapshot.MapChoiceFingerprint == null &&
            pair.Host.Snapshot.MapChoiceRevision == SessionMachine.MaxOutgoingMapPackets, "overflow lost high water or retained the source");
        Assert(pair.Host.TryTakePacket(out WirePacket retire) && retire.Kind == PacketKind.MapChoiceRetire && !pair.Host.TryTakePacket(out _),
            "overflow did not explicitly retire and cancel queued choices");
        retire.Sequence = 1; pair.Guest.Receive(retire, 0.2);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out MapChoiceSnapshot retired) && retired.Retired && retired.Route == null && retired.Choices.Length == 0,
            "receiver reused the overflowed candidate");
        pair.Host.PublishMapRoute(Route(3), 0.3); pair.Pump(0.3); pair.Guest.TryTakeRemoteMapChoices(out _);
        fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        for (int i = 1; i <= 3; i++) pair.Host.PublishMapIgpChoice(Choice(2, i, fingerprint, i == 3 ? "group-b" : "group-a"), 0.4);
        for (int i = 1; i <= 3; i++)
        {
            Assert(pair.Host.TryTakePacket(out WirePacket packet) && packet.MapChoice.Revision == i, "choice FIFO overwrote or reordered an observation");
            packet.Sequence = i + 1; pair.Guest.Receive(packet, 0.4);
        }
        Assert(pair.Guest.TryTakeRemoteMapChoices(out MapChoiceSnapshot current) && current.LastChoiceRevision == 3 && current.Choices.Length == 2,
            "same-group revisions did not overwrite the prior selection by order");
    }

    internal static void DirectionRoomAndSourceSpoofing()
    {
        var pair = new Pair(); MapRouteSelection route = Route(3);
        Throws<ProtocolException>(() => pair.Guest.PublishMapRoute(route, 0.1));
        Throws<ProtocolException>(() => pair.Guest.PublishMapIgpChoice(Choice(1, 1, MapSelections.FingerprintRoute(route)), 0.1));
        Throws<ProtocolException>(() => pair.Guest.RetireMapChoices("invalid guest", 0.1));
        Throws<ProtocolException>(() => pair.Host.TryTakeRemoteMapChoices(out _));
        WirePacket packet = Packet(pair.Room, MapChoiceFrames.SplitRoute(route, 1)[0]);
        Throws<ProtocolException>(() => pair.Host.Receive(packet, 0.1));
        packet.RoomId = Guid.NewGuid().ToString("N");
        Throws<ProtocolException>(() => pair.Guest.Receive(packet, 0.1));
        packet.RoomId = pair.Room; pair.Guest.Receive(packet, 0.1); pair.Guest.TryTakeRemoteMapChoices(out _);
        var future = new WirePacket { Kind = PacketKind.MapIgpChoice, RoomId = pair.Room, Sequence = 2,
            MapChoice = Choice(2, 1, MapSelections.FingerprintRoute(route)) };
        Throws<ProtocolException>(() => pair.Guest.Receive(future, 0.2));
        future.MapChoice.Generation = 1; future.MapChoice.RouteFingerprint = "map-route-v2/" + new string('0', 64);
        Throws<ProtocolException>(() => pair.Guest.Receive(future, 0.2));
        future.MapChoice.RouteFingerprint = MapSelections.FingerprintRoute(route); future.MapChoice.Revision = 2;
        Throws<ProtocolException>(() => pair.Guest.Receive(future, 0.2));
        Assert(pair.Guest.Snapshot.MapChoiceRevision == 0, "forged future/conflicting choice changed valid state");
    }

    internal static void SceneChangeKeepsPreloadAndCloseClears()
    {
        var pair = new Pair(); pair.Host.PublishMapRoute(Route(17), 0.1);
        var scene = new SceneDescriptor("dive", "layout"); pair.Host.SetLocalScene(scene, 0.2); pair.Guest.SetLocalScene(scene, 0.2); pair.Pump(0.2);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out MapChoiceSnapshot preload) && preload.Route.Scenes.Length == 17 &&
            pair.Host.Snapshot.Phase == SessionPhase.Ready && pair.Guest.Snapshot.Phase == SessionPhase.Ready,
            "normal scene publication canceled queued preload evidence");
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        pair.Host.PublishMapIgpChoice(Choice(1, 1, fingerprint), 0.3);
        pair.Guest.SetLocalScene(null, 0.3); pair.Pump(0.3);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out preload) && preload.Route != null && preload.LastChoiceRevision == 1 &&
            pair.Host.Snapshot.MapChoiceFingerprint == fingerprint, "ScenePause implicitly retired a selected map");
        pair.Host.PublishMapIgpChoice(Choice(1, 2, fingerprint), 0.4);
        pair.Host.Close("done"); pair.Guest.Close("done");
        Assert(!pair.Host.TryTakePacket(out _) && !pair.Guest.TryTakeRemoteMapChoices(out _) &&
            pair.Host.Snapshot.MapChoiceGeneration == 0 && pair.Guest.Snapshot.MapChoiceGeneration == 0 &&
            !pair.Host.PublishMapRoute(Route(3), 0.5), "closed room retained source packets or candidate mailbox");
    }

    internal static void ControlPriorityAndFourLaneFairness()
    {
        var pair = Pair.Ready(); pair.Host.PublishMapRoute(Route(17), 0.1);
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        for (int i = 1; i <= 20; i++)
        {
            pair.Host.PublishMapIgpChoice(Choice(1, i, fingerprint, "group-" + i), 0.1);
            var request = new FishActionRequest { RequestId = i, PlayerId = 2, SceneEpoch = 1, SceneKey = "dive", Action = FishActionKind.ProbeTarget, TargetEntityId = 44 };
            pair.Host.PublishFishActionResult(new FishActionResult
            {
                RequestId = i, PlayerId = 2, SceneEpoch = 1, SceneKey = "dive", Action = request.Action, TargetEntityId = 44,
                Status = FishActionStatus.Queued, RequestFingerprint = FishActions.Fingerprint(request)
            }, 0.1);
        }
        pair.Host.PublishFrame(Frame(0.1), 0.1); pair.Host.PublishWorld(World(0.1), 0.1); pair.Host.Tick(0.1);
        Assert(pair.Host.TryTakePacket(out WirePacket control) && control.Kind == PacketKind.Ping, "map FIFO starved heartbeat control");
        int actions = 0, frames = 0, worlds = 0, maps = 0; long lastMapRevision = 0;
        for (int i = 0; i < 60; i++)
        {
            double now = 0.2 + i * 0.01;
            pair.Host.PublishFrame(Frame(now), now); pair.Host.PublishWorld(World(now), now);
            Assert(pair.Host.TryTakePacket(out WirePacket packet), "fair scheduler lost a populated lane");
            if (packet.Kind == PacketKind.FishActionResult) actions++;
            else if (packet.Kind == PacketKind.PlayerFrame) frames++;
            else if (packet.Kind == PacketKind.WorldSlice) worlds++;
            else if (packet.Kind == PacketKind.MapRouteSlice || packet.Kind == PacketKind.MapIgpChoice)
            {
                maps++;
                if (packet.MapChoice != null) Assert(packet.MapChoice.Revision == ++lastMapRevision, "fair map lane reordered native selection observations");
            }
        }
        Assert(actions == 15 && frames == 15 && worlds == 15 && maps == 15, "four populated lanes did not each receive a bounded fair turn");
        pair.Host.RetireMapChoices("stop", 1);
        Assert(pair.Host.TryTakePacket(out control) && control.Kind == PacketKind.MapChoiceRetire, "retire was delayed behind queued gameplay/map work");
    }

    internal static void StalePublishedChoiceCanceled()
    {
        var pair = new Pair(); pair.Host.PublishMapRoute(Route(3), 0.1);
        string fingerprint = pair.Host.Snapshot.MapChoiceFingerprint;
        MapIgpChoice old = Choice(1, 1, fingerprint);
        pair.Host.RetireMapChoices("source cleared", 0.2);
        Assert(!pair.Host.RetireMapChoices("observer disabled", 0.2), "inactive source sent a conflicting duplicate retirement");
        Assert(!pair.Host.PublishMapIgpChoice(old, 0.2), "legitimate same-generation callback after retirement disconnected the room");
        var forged = Choice(1, 1, "map-route-v2/" + new string('0', 64));
        Throws<ProtocolException>(() => pair.Host.PublishMapIgpChoice(forged, 0.2));
        pair.Host.PublishMapRoute(Route(4), 0.3);
        Assert(!pair.Host.PublishMapIgpChoice(old, 0.3) && pair.Host.Snapshot.MapChoiceRevision == 0, "old source callback changed the fresh generation");
        var future = Choice(3, 1, pair.Host.Snapshot.MapChoiceFingerprint);
        Throws<ProtocolException>(() => pair.Host.PublishMapIgpChoice(future, 0.3));
        pair.Host.PublishMapIgpChoice(Choice(2, 1, pair.Host.Snapshot.MapChoiceFingerprint), 0.3);
        pair.Pump(0.3);
        Assert(pair.Guest.TryTakeRemoteMapChoices(out MapChoiceSnapshot snapshot) && snapshot.Generation == 2 && snapshot.LastChoiceRevision == 1 &&
            pair.Host.Snapshot.Phase != SessionPhase.Closed && pair.Guest.Snapshot.Phase != SessionPhase.Closed,
            "stale callback cancellation prevented same-room recovery");
    }

    internal static async Task TcpPreloadRoundTripAndRetirement()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepting;
        MapRouteSelection route = Route(32);
        Assert(host.PublishMapRoute(route), "TCP preload route was not queued"); route.Scenes[0].SceneName = "caller mutation";
        MapChoiceSnapshot received = null;
        await Until(() => guest.TryTakeRemoteMapChoices(out received) && received.Route != null, cancellation.Token);
        Assert(received.Route.Scenes.Length == 32 && received.Route.Scenes[0].SceneName == "dive-0" &&
            host.Snapshot.Phase == SessionPhase.WaitingForScene && guest.Snapshot.SceneEpoch == 0,
            "TCP atomic route changed readiness or retained the publisher array");
        string fingerprint = host.Snapshot.MapChoiceFingerprint;
        MapIgpChoice choice = Choice(1, 1, fingerprint);
        host.PublishMapIgpChoice(choice); choice.SelectedPrefabName = "caller mutation";
        await Until(() => guest.TryTakeRemoteMapChoices(out received) && received.LastChoiceRevision == 1, cancellation.Token);
        Assert(received.Choices.Length == 1 && received.Choices[0].SelectedPrefabName == "IGP_A", "TCP lost the original selection payload");
        choice = Choice(1, 2, fingerprint); choice.SelectedPrefabName = "IGP_B"; host.PublishMapIgpChoice(choice);
        await Until(() => guest.TryTakeRemoteMapChoices(out received) && received.LastChoiceRevision == 2, cancellation.Token);
        Assert(received.Choices.Length == 1 && received.Choices[0].SelectedPrefabName == "IGP_B", "TCP failed to apply continuous same-group revision");
        host.RetireMapChoices("source invalidated");
        await Until(() => guest.TryTakeRemoteMapChoices(out received) && received.Retired, cancellation.Token);
        Assert(received.Route == null && received.Choices.Length == 0 && received.ObservationOnly && !received.HostSelectionApplied &&
            host.Snapshot.MapChoiceGeneration == 1 && host.Snapshot.MapChoiceFingerprint == null, "TCP retired candidate remained usable");
        Assert(!host.PublishMapIgpChoice(Choice(1, 3, fingerprint)), "stale TCP adapter callback became a fatal send");
        host.PublishMapRoute(Route(3));
        await Until(() => guest.TryTakeRemoteMapChoices(out received) && received.Generation == 2 && received.Route != null, cancellation.Token);
        Assert(host.Snapshot.RoomId == listener.RoomId && guest.Snapshot.RoomId == listener.RoomId, "retirement forced room replacement");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
    }

    internal static void RouteWireRequiresExplicitNativeInputs()
    {
        MapRouteSelection route = Route(3); // Legal explicit zero/false inputs.
        WirePacket packet = Packet(Guid.NewGuid().ToString("N"), MapChoiceFrames.SplitRoute(route, 1)[0]);
        string encoded = Encoding.UTF8.GetString(PacketCodec.Encode(packet));
        WirePacket decoded = PacketCodec.Decode(Encoding.UTF8.GetBytes(encoded));
        Assert(decoded.MapRoute.TotalSceneHeight == 0 && decoded.MapRoute.Scenes[0].Priority == 0 &&
            decoded.MapRoute.Scenes[0].PreferenceWeight == 0 && !decoded.MapRoute.Scenes[0].PreloadAndNotUnloadable,
            "legal explicit native defaults were rejected or changed");
        string[] required = { "\"totalSceneHeight\":0", "\"priority\":0", "\"preferenceWeight\":0", "\"preloadAndNotUnloadable\":false" };
        foreach (string field in required)
        {
            RejectWireMutation(encoded, field + ",", "");
            RejectWireMutation(encoded, field, field + "," + field);
        }
        RejectWireMutation(encoded, "\"priority\":0", "\"priority\":0.5");
        RejectWireMutation(encoded, "\"preferenceWeight\":0", "\"preferenceWeight\":\"0\"");
        RejectWireMutation(encoded, "\"preloadAndNotUnloadable\":false", "\"preloadAndNotUnloadable\":0");
        RejectWireMutation(encoded, "\"totalSceneHeight\":0", "\"totalSceneHeight\":true");
        RejectWireMutation(encoded, "\"priority\":0", "\"Priority\":0");
        RejectWireMutation(encoded, "map-route-v2/", "map-route-v1/");
    }

    internal static async Task TcpNativeRouteInputsRoundTripAndReplacement()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        using SessionPeer guest = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, Identity("Guest"), cancellation.Token);
        using SessionPeer host = await accepting;
        MapRouteSelection source = Route(17); source.TotalSceneHeight = 7521.25f;
        source.Scenes[0].Priority = int.MinValue; source.Scenes[0].PreferenceWeight = int.MaxValue;
        source.Scenes[0].PreloadAndNotUnloadable = true;
        source.Scenes[16].Priority = 3; source.Scenes[16].PreferenceWeight = 9;
        MapRouteSelection expected = MapSelections.CopyRoute(source);
        string fingerprint = MapSelections.FingerprintRoute(expected);
        Assert(host.PublishMapRoute(source), "TCP host rejected a complete native input route");
        source.TotalSceneHeight = 0; source.Scenes[0].Priority = 0; source.Scenes[0].PreferenceWeight = 0;
        source.Scenes[0].PreloadAndNotUnloadable = false; source.Scenes[16] = null;
        MapChoiceSnapshot received = null;
        await Until(() => guest.TryTakeRemoteMapChoices(out received) && received.Route != null, cancellation.Token);
        Assert(received.RouteFingerprint == fingerprint && received.Route.TotalSceneHeight == 7521.25f &&
            received.Route.Scenes[0].Priority == int.MinValue && received.Route.Scenes[0].PreferenceWeight == int.MaxValue &&
            received.Route.Scenes[0].PreloadAndNotUnloadable && received.Route.Scenes[16].Priority == 3 &&
            received.Route.Scenes[16].PreferenceWeight == 9, "TCP copy/split/assembler lost or shared native fields");
        received.Route.TotalSceneHeight = 1; received.Route.Scenes[0].Priority = 1;
        received.Route.Scenes[0].PreloadAndNotUnloadable = false;
        Assert(host.PublishMapIgpChoice(Choice(1, 1, fingerprint)), "TCP route could not accept a separate observed IGP choice");
        await Until(() => guest.TryTakeRemoteMapChoices(out received) && received.LastChoiceRevision == 1, cancellation.Token);
        Assert(received.Route.TotalSceneHeight == 7521.25f && received.Route.Scenes[0].Priority == int.MinValue &&
            received.Route.Scenes[0].PreloadAndNotUnloadable, "a consumer mutation changed the receiver's stored native route");
        expected.Scenes[0].PreferenceWeight--; expected.TotalSceneHeight += 0.25f;
        string replacementFingerprint = MapSelections.FingerprintRoute(expected);
        Assert(replacementFingerprint != fingerprint && host.PublishMapRoute(expected), "changed native input failed to publish a fresh route identity");
        await Until(() => guest.TryTakeRemoteMapChoices(out received) && received.Generation == 2 && received.Route != null, cancellation.Token);
        Assert(received.RouteFingerprint == replacementFingerprint && received.Route.TotalSceneHeight == 7521.5f &&
            received.Route.Scenes[0].PreferenceWeight == int.MaxValue - 1 && received.Choices.Length == 0 &&
            received.LastChoiceRevision == 0 && received.ObservationOnly && !received.HostSelectionApplied &&
            host.Snapshot.Phase == SessionPhase.WaitingForScene && guest.Snapshot.SceneEpoch == 0,
            "TCP replacement retained old IGP choices or falsely established applied-world readiness");
        await guest.StopAsync(); await host.Completion.WaitAsync(cancellation.Token);
    }

    internal static async Task RejectProtocolSix()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new LanHost(IPAddress.Loopback, 0);
        PeerIdentity legacy = Identity("Guest"); legacy.ProtocolVersion = 6;
        Task<SessionPeer> accepting = listener.AcceptOneAsync(Identity("Host"), cancellation.Token);
        bool guestRejected = false, hostRejected = false;
        try { using SessionPeer unexpected = await LanGuest.ConnectAsync("127.0.0.1", listener.Port, legacy, cancellation.Token); }
        catch (ProtocolException error) { guestRejected = error.Message.Contains("Protocol version mismatch"); }
        try { using SessionPeer unexpected = await accepting; }
        catch (ProtocolException error) { hostRejected = error.Message.Contains("Protocol version mismatch"); }
        Assert(guestRejected && hostRejected, "protocol 6 silently accepted incomplete native route inputs");
    }

    private static void RejectWireMutation(string original, string from, string to)
    {
        string changed = original.Replace(from, to, StringComparison.Ordinal);
        Assert(changed != original, "wire corruption fixture did not change its required field");
        Throws<ProtocolException>(() => PacketCodec.Decode(Encoding.UTF8.GetBytes(changed)));
    }

    private static MapRouteSelection Route(int count)
    {
        var scenes = new MapRouteScene[count];
        for (int i = 0; i < count; i++) scenes[i] = new MapRouteScene
        {
            SceneId = 1000 + i, SceneName = "dive-" + i, Layer = 'A', MapHeight = 500, TopY = 0, BottomY = -500, Offset = i * -500,
            PreviousSceneId = i == 0 ? 0 : 999 + i, NextSceneId = i == count - 1 ? 0 : 1001 + i
        };
        return new MapRouteSelection { EntrySceneId = 1000, Scenes = scenes };
    }
    private static MapIgpChoice Choice(long generation, long revision, string fingerprint, string group = "group-a") => new MapIgpChoice
    {
        Generation = generation, Revision = revision, RouteFingerprint = fingerprint, SceneId = 1000,
        ControllerAddress = "RuntimeObjects/" + group, Addressable = true, SelectedPrefabName = "IGP_A", PrefabObjectName = ""
    };
    private static WirePacket Packet(string room, MapRouteSlice slice) => new WirePacket
    { Kind = PacketKind.MapRouteSlice, Sequence = 1, RoomId = room, MapRoute = MapChoiceFrames.Copy(slice) };
    private static PlayerFrame Frame(double time) => new PlayerFrame
    { SceneKey = "dive", SampleTime = time, Root = new Pose { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One } };
    private static WorldSnapshot World(double time)
    {
        var entities = new EntityState[65];
        for (int i = 0; i < entities.Length; i++) entities[i] = new EntityState
        {
            Id = i + 1, Kind = EntityKind.Fish, DataTid = 2010007, Hp = 10, MaxHp = 10,
            Root = new Pose { Position = new Vector3(i, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One }
        };
        return new WorldSnapshot { SceneEpoch = 1, SceneKey = "dive", Revision = 1, SampleTime = time, Entities = entities };
    }
    private static PeerIdentity Identity(string name) => new PeerIdentity
    { ModVersion = "0.1.14-dev", SteamBuildId = "25315876", UnityVersion = "6000.0.52f1", Name = name };
    private static async Task Until(Func<bool> condition, CancellationToken cancellation)
    { while (!condition()) await Task.Delay(5, cancellation); }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private sealed class Pair
    {
        public readonly string Room = Guid.NewGuid().ToString("N");
        public readonly SessionMachine Host;
        public readonly SessionMachine Guest;
        private long _sequence;
        public Pair()
        {
            Host = new SessionMachine(SessionRole.Host, new HandshakeResult { RoomId = Room, LocalPlayerId = 1, RemotePlayerId = 2 }, 0);
            Guest = new SessionMachine(SessionRole.Guest, new HandshakeResult { RoomId = Room, LocalPlayerId = 2, RemotePlayerId = 1 }, 0);
        }
        public static Pair Ready()
        {
            var pair = new Pair(); var scene = new SceneDescriptor("dive", "layout");
            pair.Host.SetLocalScene(scene, 0); pair.Guest.SetLocalScene(scene, 0); pair.Pump(0);
            Assert(pair.Host.Snapshot.Phase == SessionPhase.Ready && pair.Guest.Snapshot.Phase == SessionPhase.Ready, "fixture did not commit scene");
            return pair;
        }
        public void Pump(double now)
        {
            bool progress;
            do
            {
                progress = false;
                while (Host.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Guest.Receive(packet, now); progress = true; }
                while (Guest.TryTakePacket(out WirePacket packet)) { packet.Sequence = ++_sequence; Host.Receive(packet, now); progress = true; }
            } while (progress);
        }
    }
}
