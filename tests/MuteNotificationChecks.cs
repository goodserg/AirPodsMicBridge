using System;
using AirPodsMicBridge;

// Exercises real notification sequences without any audio or microphone access.
internal static class MuteNotificationChecks
{
    private static int checks;

    public static int Main()
    {
        try
        {
            CheckPassiveObservation();
            CheckDelayedNativeGesture();
            CheckSuccessfulOperation();
            CheckBaselineLoss();
            CheckOperationWithoutBaseline();
            Console.WriteLine("Mute notification checks passed: " + checks);
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

    private static void CheckPassiveObservation()
    {
        var state = new MuteNotifications();
        Assert(!state.Observe("airpods", false, true), "Startup live state is a silent baseline");
        Assert(!state.Observe("airpods", false, true), "Unchanged polling is silent");
        Assert(state.Observe("airpods", true, true), "External mute is announced");
        Assert(!state.Observe("airpods", true, true), "Repeated mute polling is silent");
        Assert(state.Observe("airpods", false, true), "External unmute is announced");
        Assert(!state.Observe("airpods", false, true), "Repeated live polling is silent");

        var mutedStartup = new MuteNotifications();
        Assert(!mutedStartup.Observe("airpods", true, true), "Startup muted state is also silent");
        Assert(mutedStartup.Observe("airpods", false, true), "First later unmute is announced");
    }

    private static void CheckDelayedNativeGesture()
    {
        var state = new MuteNotifications();
        Assert(!state.Observe("airpods", false, true), "Establish native-gesture baseline");
        Assert(state.Observe("airpods", true, true), "Polling announces native mute before delayed media command");
        Assert(!state.Observe("airpods", true, true), "Next worker iteration sees the same mute");
        Assert(!state.Confirm("airpods", true), "Preserving delayed native mute must not announce a second time");
        Assert(!state.Confirm("airpods", true), "Repeated confirmation remains silent");
        Assert(state.Observe("airpods", false, true), "Later native unmute is a new transition");
        Assert(!state.Confirm("airpods", false), "Delayed native unmute confirmation is deduplicated too");
        Assert(state.Confirm("airpods", true), "A new mute operation is still announced");
    }

    private static void CheckSuccessfulOperation()
    {
        var state = new MuteNotifications();
        Assert(!state.Observe("airpods", false, true), "Establish bridge-operation baseline");
        Assert(state.Confirm("airpods", true), "Successful mute operation is announced");
        Assert(!state.Observe("airpods", true, true), "Readback after mute must not repeat the sound");
        Assert(!state.Observe("airpods", true, true), "Later repeated mute polling stays silent");
        Assert(state.Confirm("airpods", false), "Successful unmute operation is announced");
        Assert(!state.Observe("airpods", false, true), "Readback after unmute must not repeat the sound");
        // No time debounce may eat a real, rapidly alternating sequence.
        for (int index = 0; index < 10; index++)
            Assert(state.Confirm("airpods", index % 2 == 0), "Rapid alternating confirmed change " + index);
        Assert(!state.Confirm("airpods", false), "Same final confirmation is silent");
    }

    private static void CheckBaselineLoss()
    {
        var state = new MuteNotifications();
        Assert(!state.Observe("first", false, true), "First endpoint baseline");
        Assert(!state.Observe("second", true, true), "Switching endpoints must be silent");
        Assert(state.Observe("second", false, true), "Change on the newly observed endpoint is announced");
        Assert(!state.Observe(null, null, false), "Disconnected state resets silently");
        Assert(!state.Observe("second", true, true), "Reconnection establishes a silent baseline");
        Assert(!state.Observe("second", null, true), "Unknown mute resets silently");
        Assert(!state.Observe("second", false, true), "Recovery from unknown state is silent");
        Assert(!state.Observe("second", true, false), "No active capture, ambiguity or paused control resets silently");
        Assert(!state.Observe("second", true, true), "Resuming eligibility must not announce a change while paused");
        Assert(state.Observe("second", false, true), "Next real active transition is announced");
        state.Reset();
        Assert(!state.Observe("second", true, true), "Explicit diagnostic/control suspension resets baseline");
        Assert(state.Confirm("second", false), "A confirmed operation after suspension is announced");
        Assert(!state.Observe(" ", true, true), "An invalid endpoint resets silently");
        Assert(!state.Observe("second", false, true), "A valid endpoint after invalid identity establishes baseline");
    }

    private static void CheckOperationWithoutBaseline()
    {
        var state = new MuteNotifications();
        Assert(state.Confirm("airpods", true), "Explicit successful mute is audible without a passive baseline");
        Assert(!state.Observe("airpods", true, true), "Later observation of confirmed mute is silent");
        state.Reset();
        Assert(state.Confirm("airpods", false), "Explicit successful unmute is audible without a passive baseline");
        Assert(!state.Confirm("airpods", false), "No-baseline exception does not permit repeated confirmation");
        Assert(state.Confirm("other", false), "An explicit successful operation on another endpoint is announced");
        Assert(!state.Observe("other", false, true), "New target observation does not replay the explicit operation");
        Assert(!state.Confirm(null, true), "Invalid confirmed identity never announces");
        Assert(!state.Observe("other", true, true), "Invalid confirmation resets the passive baseline");
    }
}
