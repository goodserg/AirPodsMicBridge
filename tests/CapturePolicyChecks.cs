using System;
using AirPodsMicBridge;

// Pure snapshot fixtures only: this entry point never opens Core Audio or changes mute.
internal static class CapturePolicyChecks
{
    private static int checks;

    public static int Main(string[] args)
    {
        try
        {
            CheckSelection();
            CheckCanArm();
            CheckInputPreservation();
            Console.WriteLine("Capture policy checks passed: " + checks);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static CaptureEndpointSnapshot Endpoint(string id, bool airPods, int active, bool? muted, string error = null)
    {
        return new CaptureEndpointSnapshot
        {
            EndpointId = id, FriendlyName = airPods ? "AirPods fixture" : "Other capture fixture",
            IsAirPods = airPods, ActiveSessionCount = active, Muted = muted, Error = error
        };
    }

    private static void Expect(string name, CaptureEndpointSnapshot[] endpoints, string id, string error, string pinned = null)
    {
        AudioStateSnapshot state = CoreAudioAdapter.SelectTarget(endpoints, pinned);
        Assert(String.Equals(state.Error, error, StringComparison.Ordinal), name + ": unexpected selection error " + state.Error);
        Assert(String.Equals(state.EndpointId, id, StringComparison.Ordinal), name + ": unexpected target " + state.EndpointId);
        Assert(state.CanArm == (id != null), name + ": incorrect handling eligibility");
        Assert(state.CaptureEndpoints != null && state.CaptureEndpoints.Length == (endpoints == null ? 0 : endpoints.Length), name + ": discovery rows were lost");
        if (id == null) Assert(state.Target == null, name + ": failed selection must not retain a target");
    }

    private static void CheckSelection()
    {
        Expect("null discovery", null, null, "NoAirPodsCaptureEndpoint");
        Expect("empty discovery", new CaptureEndpointSnapshot[0], null, "NoAirPodsCaptureEndpoint");
        Expect("null row", new CaptureEndpointSnapshot[] { null }, null, "NoAirPodsCaptureEndpoint");
        Expect("other active microphone", new[] { Endpoint("other", false, 1, false) }, null, "NoAirPodsCaptureEndpoint");
        Expect("other unreadable microphone", new[] { Endpoint(null, false, 1, null, "AccessDenied") }, null, "NoAirPodsCaptureEndpoint");
        Expect("connected inactive AirPods", new[] { Endpoint("air", true, 0, false) }, null, "NoActiveAirPodsCapture");
        Expect("inactive muted AirPods", new[] { Endpoint("air", true, 0, true) }, null, "NoActiveAirPodsCapture");
        Expect("negative count is inactive", new[] { Endpoint("air", true, -1, false) }, null, "NoActiveAirPodsCapture");
        Expect("active unmuted AirPods", new[] { Endpoint("air", true, 1, false) }, "air", null);
        Expect("active muted AirPods", new[] { Endpoint("air", true, 1, true) }, "air", null);
        Expect("several apps on one endpoint", new[] { Endpoint("air", true, 4, false) }, "air", null);
        Expect("other app uses another microphone", new[] { Endpoint("other", false, 3, false), Endpoint("air", true, 1, true) }, "air", null);
        Expect("non-AirPods error does not block", new[] { Endpoint(null, false, 3, null, "DeviceInvalidated"), Endpoint("air", true, 1, false) }, "air", null);
        Expect("one active and one idle AirPods", new[] { Endpoint("idle", true, 0, false), Endpoint("air", true, 2, false) }, "air", null);
        var idleDefault = Endpoint("idle", true, 0, false);
        idleDefault.DefaultCommunications = true;
        Expect("idle default must not win", new[] { idleDefault, Endpoint("air", true, 1, false) }, "air", null);
        var activeDefault = Endpoint("air", true, 1, false);
        activeDefault.DefaultCommunications = true;
        Expect("two active AirPods", new[] { Endpoint("second", true, 1, true), activeDefault }, null, "AmbiguousAirPodsCaptureEndpoints");
        Expect("three active AirPods", new[] { Endpoint("first", true, 1, false), Endpoint("second", true, 2, false), Endpoint("third", true, 1, false) }, null, "AmbiguousAirPodsCaptureEndpoints");
        Expect("AirPods session error", new[] { Endpoint("air", true, 1, false, "SessionUnavailable") }, null, "AirPodsCaptureStateUnavailable");
        Expect("unknown active AirPods mute", new[] { Endpoint("air", true, 1, null) }, null, "AirPodsCaptureStateUnavailable");
        Expect("missing AirPods ID", new[] { Endpoint(null, true, 1, false) }, null, "AirPodsCaptureStateUnavailable");
        Expect("empty AirPods ID", new[] { Endpoint("", true, 1, false) }, null, "AirPodsCaptureStateUnavailable");
        Expect("unknown second endpoint activity", new[] { Endpoint("air", true, 1, false), Endpoint("unknown", true, 0, false, "SessionUnavailable") }, null, "AirPodsCaptureStateUnavailable");
        Expect("unknown idle endpoint mute", new[] { Endpoint("air", true, 1, false), Endpoint("unknown", true, 0, null) }, null, "AirPodsCaptureStateUnavailable");
        Expect("unknown idle endpoint ID", new[] { Endpoint("air", true, 1, false), Endpoint(null, true, 0, false) }, null, "AirPodsCaptureStateUnavailable");
        Expect("unavailable outranks apparent ambiguity", new[] { Endpoint("one", true, 1, false), Endpoint("two", true, 1, false), Endpoint("unknown", true, 0, null) }, null, "AirPodsCaptureStateUnavailable");
        Expect("pinned matching active endpoint", new[] { Endpoint("air", true, 1, true) }, "air", null, "air");
        Expect("pinned different endpoint", new[] { Endpoint("air", true, 1, false) }, null, "PinnedCaptureEndpointNotInUse", "old");
        Expect("pinned IDs are ordinal", new[] { Endpoint("air", true, 1, false) }, null, "PinnedCaptureEndpointNotInUse", "AIR");
        Expect("pinned idle endpoint must not win", new[] { Endpoint("idle", true, 0, false), Endpoint("air", true, 1, false) }, null, "PinnedCaptureEndpointNotInUse", "idle");
        Expect("pinned endpoint lost activity", new[] { Endpoint("air", true, 0, true) }, null, "NoActiveAirPodsCapture", "air");
        Expect("pinned disconnected endpoint", new CaptureEndpointSnapshot[0], null, "NoAirPodsCaptureEndpoint", "air");
        Expect("pin cannot resolve ambiguity", new[] { Endpoint("air", true, 1, false), Endpoint("second", true, 1, false) }, null, "AmbiguousAirPodsCaptureEndpoints", "air");
        Expect("pin cannot bypass unreadable AirPods", new[] { Endpoint("air", true, 1, false), Endpoint("unknown", true, 0, null) }, null, "AirPodsCaptureStateUnavailable", "air");
    }

    private static void CheckCanArm()
    {
        CaptureEndpointSnapshot row = Endpoint("air", true, 1, false);
        var state = new AudioStateSnapshot { Target = row };
        Assert(state.CanArm && state.HasActiveCapture, "One running capture session must qualify without process metadata");
        row.Muted = true;
        Assert(state.CanArm && state.EndpointMuted == true, "Muted capture must remain eligible for re-enabling");
        row.Muted = null;
        Assert(!state.CanArm, "Unknown mute state must fail closed");
        row.Muted = false;
        row.ActiveSessionCount = 0;
        Assert(!state.CanArm && !state.HasActiveCapture, "Stopped capture must be ineligible");
        row.ActiveSessionCount = 1;
        row.IsAirPods = false;
        Assert(!state.CanArm, "Active non-AirPods capture must be ineligible");
        row.IsAirPods = true;
        row.EndpointId = null;
        Assert(!state.CanArm, "Missing endpoint ID must be ineligible");
        row.EndpointId = "";
        Assert(!state.CanArm, "Empty endpoint ID must be ineligible");
        row.EndpointId = "air";
        row.Error = "ReadFailed";
        Assert(!state.CanArm, "Endpoint errors must fail closed");
        row.Error = null;
        state.Error = "AmbiguousAirPodsCaptureEndpoints";
        Assert(!state.CanArm, "Discovery errors must fail closed");
        state.Error = null;
        Assert(state.CanArm, "Clearing errors must restore eligibility for the valid snapshot");
        state.Target = null;
        Assert(!state.CanArm && !state.HasActiveCapture && !state.EndpointMuted.HasValue, "Missing target must remain ineligible");
    }

    private static void CheckInputPreservation()
    {
        CaptureEndpointSnapshot idle = Endpoint("z-idle", true, 0, true);
        CaptureEndpointSnapshot live = Endpoint("a-live", true, 2, false);
        var rows = new[] { idle, live };
        AudioStateSnapshot selected = CoreAudioAdapter.SelectTarget(rows);
        Assert(Object.ReferenceEquals(rows[0], idle) && Object.ReferenceEquals(rows[1], live), "Selection must not reorder caller rows");
        Assert(!Object.ReferenceEquals(selected.CaptureEndpoints, rows), "Result must own its array container");
        Assert(Object.ReferenceEquals(selected.Target, live), "Selected endpoint metadata must be preserved");
        Assert(idle.Muted == true && live.Muted == false && idle.ActiveSessionCount == 0 && live.ActiveSessionCount == 2, "Selection must not change mute or session count");
        CoreAudioAdapter.SelectTarget(rows, "z-idle");
        Assert(idle.Muted == true && live.Muted == false, "Cancelled pin revalidation must preserve mute state");
        live.ActiveSessionCount = 0;
        AudioStateSnapshot stopped = CoreAudioAdapter.SelectTarget(rows, "a-live");
        Assert(!stopped.CanArm && live.Muted == false && idle.Muted == true, "Capture stop must not change mute state");
        live.ActiveSessionCount = 1;
        live.Muted = true;
        AudioStateSnapshot resumed = CoreAudioAdapter.SelectTarget(rows, "a-live");
        Assert(resumed.CanArm && resumed.EndpointMuted == true, "Capture resume must preserve existing mute and allow an explicit reverse toggle");
    }
}
