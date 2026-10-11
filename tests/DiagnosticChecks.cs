using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using AirPodsMicBridge;

// All readers are in-memory fixtures: no COM, microphone changes, WMI or raw input registration.
internal static class DiagnosticChecks
{
    private static int checks;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };

    [MTAThread]
    public static int Main(string[] args)
    {
        try
        {
            string configured = Environment.GetEnvironmentVariable("AIRPODS_MIC_BRIDGE_DATA");
            Assert(!String.IsNullOrWhiteSpace(configured), "Set an isolated AIRPODS_MIC_BRIDGE_DATA before running checks.");
            string root = Path.Combine(Path.GetFullPath(configured), "diagnostic-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            CheckFiltersAndSummary(root);
            CheckTransitions(root);
            CheckReadFailureRedaction(root);
            CheckStopWithoutWaitingForAudio(root);
            CheckInventoryDispatch(root);
            CheckStopWithoutWaitingForInventory(root);
            CheckLimits(root);
            Console.WriteLine("Diagnostic checks passed: " + checks);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static DiagnosticSessionOptions Options(string root, Func<AudioStateSnapshot> reader)
    {
        return new DiagnosticSessionOptions { DataDirectory = root, ReadAudioState = reader,
            ReadInventory = () => null, PollMilliseconds = 20 };
    }

    private static AudioStateSnapshot State(int selectedSessions, bool? selectedMute)
    {
        return new AudioStateSnapshot {
            Error = "NoAirPodsCaptureActive",
            CaptureEndpoints = new[] {
                new CaptureEndpointSnapshot { EndpointId = "other-endpoint", FriendlyName = "Other capture", IsAirPods = true, Muted = false, ActiveSessionCount = 0 },
                new CaptureEndpointSnapshot { EndpointId = "fixture-selected", FriendlyName = "Non-AirPods headset", IsAirPods = false,
                    Muted = selectedMute, ActiveSessionCount = selectedSessions, DefaultCommunications = true }
            }
        };
    }

    private static void CheckFiltersAndSummary(string root)
    {
        var session = new DiagnosticSession("fixture-selected", "Non-AirPods headset", Options(root, () => State(2, false)));
        using (session)
        {
            session.RecordMediaKey(0xB3, 1, 0, true); // Ignored before Start.
            session.Start();
            Assert(session.IsRunning && !session.IsComplete, "Start must publish running state.");
            Wait(() => session.EventCount >= 1, 3000, "Initial capture snapshot was not written.");
            bool duplicateStartRejected = false;
            try { session.Start(); } catch (InvalidOperationException) { duplicateStartRejected = true; }
            Assert(duplicateStartRejected, "A session must not create two writers.");
            foreach (int key in new[] { 0x00, 0x41, 0x0D, 0xE7, 0xFFFF }) session.RecordMediaKey(key, 1, 0, true);
            foreach (int key in new[] { 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3 }) session.RecordMediaKey((uint)key, 5U, 0x10U, true);
            session.RecordSequenceStep(0);
            session.RecordSequenceStep(7);
            session.RecordSequenceStep(1);
            session.RecordSequenceStep(6);
            session.RecordRawInput(new DiagnosticRawInput { Kind = "report", DeviceType = "keyboard", DeviceName = "SECRET_KEYBOARD", Hex = "414243" });
            session.RecordRawInput(new DiagnosticRawInput { Kind = "report", DeviceType = "mouse", DeviceName = "SECRET_MOUSE", Hex = "FFAA" });
            session.RecordRawInput(new DiagnosticRawInput { Kind = "report", DeviceType = "HID", DeviceName = "Generic desktop", UsagePage = 1, Usage = 6, Hex = "ABCD" });
            var copied = new DiagnosticRawInput { Kind = "report", DeviceType = "HID", DeviceName = "original device", UsagePage = 0x0C,
                Usage = 1, VendorId = 76, ProductId = 1, ReportSize = 3, ReportCount = 1, Hex = "01 ff 00" };
            session.RecordRawInput(copied);
            copied.DeviceName = "MUTATED_AFTER_QUEUE";
            copied.Hex = "INVALID_AFTER_QUEUE";
            session.RecordRawInput(new DiagnosticRawInput { Kind = "report", DeviceType = "HID", UsagePage = 0x0B, Usage = 1,
                ReportSize = 4100, ReportCount = 1, Hex = new String('F', 8200) });
            session.RecordRawInput(new DiagnosticRawInput { Kind = "report", DeviceType = "HID", UsagePage = 0x0C, Hex = "SECRET_INVALID_HEX" });
            session.RecordRawInput(new DiagnosticRawInput { Kind = "device-inventory", DeviceType = "HID", UsagePage = 0x0C, Hex = "BEEF" });
            session.RecordRawInput(new DiagnosticRawInput { Kind = "raw-input-error", Error = "FixtureRawInputError" });
            session.RecordRawInput(new DiagnosticRawInput { Kind = "unregistration-error", Error = "FixtureUnregistrationError" });
            var inventory = new DiagnosticInventoryResult();
            inventory.Drivers.Add(new Dictionary<string, object> {
                { "DeviceName", "Fixture driver" }, { "DriverVersion", "1.0" }, { "IsSigned", true },
                { "UnapprovedField", "SECRET_UNAPPROVED_FIELD" }
            });
            session.RecordInventory(inventory);
            Assert(session.Stop(), "Stop must close the writer before export.");
            Assert(session.IsComplete && !session.IsRunning && session.Error == null, "Stopped session state is incorrect.");
            Assert(session.Stop(), "Stop must be idempotent.");
            string text = File.ReadAllText(session.LogPath);
            Assert(!text.Contains("SECRET_KEYBOARD") && !text.Contains("SECRET_MOUSE"), "Raw ordinary keyboard or mouse data was logged.");
            Assert(!text.Contains("MUTATED_AFTER_QUEUE") && !text.Contains("INVALID_AFTER_QUEUE"), "Raw event data was not copied before enqueue.");
            Assert(!text.Contains("SECRET_INVALID_HEX") && !text.Contains("SECRET_UNAPPROVED_FIELD"), "Unsafe payload or unapproved inventory field was serialized.");
            var rows = ReadRows(session.LogPath);
            Assert(Kind(rows[0]) == "session-start" && Kind(rows[rows.Count - 1]) == "session-stop", "JSONL must have a header and a final summary.");
            var header = Value(rows[0]);
            Assert((string)header["selectedEndpointId"] == "fixture-selected" && !(bool)header["audioRecording"] && !(bool)header["muteChanges"], "Header selection or read-only contract is missing.");
            Assert((string)header["processArchitecture"] == "x64" && header.ContainsKey("windowsUiLanguage") && header.ContainsKey("applicationVersion"), "Header environment fields are missing.");
            Assert(Count(rows, "media-key") == 7 && Count(rows, "sequence-step") == 2, "Media-key or sequence-step whitelist is incorrect.");
            Assert(Count(rows, "raw-input") == 7 && Count(rows, "driver-inventory") == 1, "Raw/driver inventory events are missing or duplicated.");
            var audio = Value(Find(rows, "audio-state"));
            var selected = (Dictionary<string, object>)audio["selected"];
            Assert((bool)audio["selectedPresent"] && !(bool)selected["isAirPods"] && Convert.ToInt32(selected["activeSessionCount"]) == 2,
                "Diagnostic selection must support a non-AirPods endpoint by exact ID.");
            bool normalized = false, capped = false, omitted = false, inventoryPayloadAbsent = false;
            foreach (var row in rows)
            {
                if (Kind(row) != "raw-input") continue;
                var value = Value(row);
                string hex = value["hex"] as string;
                if (hex == "01FF00") normalized = true;
                if (hex != null && hex.Length == 8192) capped = true;
                if ((string)value["deviceName"] == "Generic desktop" && hex == null && (bool)value["payloadOmitted"]) omitted = true;
                if ((string)value["kind"] == "device-inventory" && hex == null) inventoryPayloadAbsent = true;
            }
            Assert(normalized && capped && omitted && inventoryPayloadAbsent, "HID hex whitelist, normalization or byte cap failed.");
            CheckSummary(rows, session);
            long count = session.EventCount;
            session.RecordMediaKey(0xB3, 1, 0, true);
            Assert(session.EventCount == count, "Events after Stop must be ignored.");
        }
    }

    private static void CheckTransitions(string root)
    {
        int stage = 0;
        Func<AudioStateSnapshot> reader = () => {
            int current = Interlocked.CompareExchange(ref stage, 0, 0);
            if (current == 2) return new AudioStateSnapshot { CaptureEndpoints = new CaptureEndpointSnapshot[0], Error = "FixtureRemoved" };
            var state = State(current, current == 0 ? (bool?)false : null);
            if (current == 1) state.CaptureEndpoints[1].Error = "FixtureUnknownMute";
            return state;
        };
        using (var session = new DiagnosticSession("fixture-selected", "Non-AirPods headset", Options(root, reader)))
        {
            session.Start();
            Wait(() => session.EventCount >= 1, 3000, "Baseline state missing.");
            Thread.Sleep(100);
            Assert(session.EventCount == 1, "Repeated unchanged snapshots should not flood the log.");
            Interlocked.Exchange(ref stage, 1);
            Wait(() => session.EventCount >= 2, 3000, "Mute/session/error transition missing.");
            Interlocked.Exchange(ref stage, 2);
            Wait(() => session.EventCount >= 3, 3000, "Endpoint removal transition missing.");
            Assert(session.Stop(), "Transition session did not stop.");
            var rows = ReadRows(session.LogPath);
            Assert(Count(rows, "audio-state") == 3, "Unexpected audio transition count.");
            var last = Value(FindLast(rows, "audio-state"));
            Assert(!(bool)last["selectedPresent"] && last["selected"] == null && (string)last["adapterError"] == "FixtureRemoved", "Removed selected endpoint was not represented.");
            CheckSummary(rows, session);
        }
    }

    private static void CheckReadFailureRedaction(string root)
    {
        using (var session = new DiagnosticSession("fixture-selected", "Headset", Options(root, () => { throw new InvalidOperationException("SECRET_READ_ERROR"); })))
        {
            session.Start();
            Wait(() => session.EventCount >= 1, 3000, "Read failure was not observed.");
            Assert(session.Stop(), "Read-failure session did not stop.");
            string text = File.ReadAllText(session.LogPath);
            Assert(text.Contains("audio-read-error") && text.Contains("InvalidOperationException:0x") && !text.Contains("SECRET_READ_ERROR"), "Exception messages must not leak into diagnostics.");
            CheckSummary(ReadRows(session.LogPath), session);
        }
    }

    private static void CheckStopWithoutWaitingForAudio(string root)
    {
        using (var entered = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        using (var left = new ManualResetEvent(false))
        using (var session = new DiagnosticSession("fixture-selected", "Headset", Options(root, () => {
            entered.Set();
            release.WaitOne(5000);
            left.Set();
            return State(1, false);
        })))
        {
            session.Start();
            Assert(entered.WaitOne(3000), "Blocked-reader fixture did not enter.");
            try
            {
                var watch = Stopwatch.StartNew();
                Assert(session.Stop(), "A blocked audio reader must not prevent log export.");
                Assert(watch.ElapsedMilliseconds < 1500, "Stop waited for the blocked audio reader.");
                Assert(Count(ReadRows(session.LogPath), "audio-state") == 0, "A cancelled late audio result was logged.");
            }
            finally { release.Set(); Assert(left.WaitOne(3000), "Blocked-reader fixture did not release."); }
        }
    }

    private static void CheckLimits(string root)
    {
        var sizeOptions = Options(root, () => State(0, false));
        sizeOptions.MaximumBytes = 8192;
        sizeOptions.MaximumQueuedEvents = 2;
        using (var session = new DiagnosticSession("fixture-selected", "Headset", sizeOptions))
        {
            session.Start();
            var fill = Stopwatch.StartNew();
            int sequence = 0;
            while (session.IsRunning && fill.ElapsedMilliseconds < 4000)
            {
                for (int index = 0; index < 100; index++) session.RecordMediaKey(0xB3, sequence++, 0, true);
                Thread.Sleep(5);
            }
            Wait(() => session.IsComplete, 4000, "Size cap did not complete the session.");
            Assert(new FileInfo(session.LogPath).Length <= sizeOptions.MaximumBytes, "JSONL exceeded its byte cap.");
            var rows = ReadRows(session.LogPath);
            var summary = Value(rows[rows.Count - 1]);
            Assert((string)summary["reason"] == "size-limit" && session.DroppedEventCount > 0, "Size stop/drop summary is missing.");
            CheckSummary(rows, session);
        }
        var durationOptions = Options(root, () => State(0, false));
        durationOptions.MaximumSeconds = 1;
        using (var session = new DiagnosticSession("fixture-selected", "Headset", durationOptions))
        {
            session.Start();
            Wait(() => session.IsComplete, 4000, "Duration cap did not complete the session.");
            var rows = ReadRows(session.LogPath);
            Assert((string)Value(rows[rows.Count - 1])["reason"] == "duration-limit", "Duration stop reason is missing.");
            CheckSummary(rows, session);
        }
        using (var session = new DiagnosticSession("fixture-selected", "Headset", Options(root, () => State(0, false))))
        {
            Assert(session.Stop(), "An unstarted session must stop cleanly.");
            bool rejected = false;
            try { session.Start(); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "A cancelled session must not start later.");
        }
    }

    private static void CheckInventoryDispatch(string root)
    {
        int calls = 0;
        var options = Options(root, () => State(0, false));
        options.ReadInventory = () => {
            Interlocked.Increment(ref calls);
            var result = new DiagnosticInventoryResult();
            result.Drivers.Add(new Dictionary<string, object> {
                { "DeviceName", "Automatically collected fixture" }, { "DeviceClass", "HIDClass" },
                { "DriverVersion", "2.0" }, { "IsSigned", true }
            });
            return result;
        };
        using (var session = new DiagnosticSession("fixture-selected", "Headset", options))
        {
            session.Start();
            Wait(() => session.EventCount >= 2, 3000, "Start did not dispatch and log driver inventory.");
            Assert(session.Stop(), "Inventory session did not stop.");
            var rows = ReadRows(session.LogPath);
            Assert(calls == 1 && Count(rows, "driver-inventory") == 1, "Driver inventory must run once per session.");
            Assert(File.ReadAllText(session.LogPath).Contains("Automatically collected fixture"), "Collected driver fields were not written.");
            CheckSummary(rows, session);
        }
    }

    private static void CheckStopWithoutWaitingForInventory(string root)
    {
        using (var entered = new ManualResetEvent(false))
        using (var release = new ManualResetEvent(false))
        using (var left = new ManualResetEvent(false))
        {
            var options = Options(root, () => State(0, false));
            options.ReadInventory = () => {
                entered.Set();
                release.WaitOne(5000);
                left.Set();
                return new DiagnosticInventoryResult();
            };
            using (var session = new DiagnosticSession("fixture-selected", "Headset", options))
            {
                session.Start();
                Assert(entered.WaitOne(3000), "Blocked-inventory fixture did not enter.");
                try
                {
                    var watch = Stopwatch.StartNew();
                    Assert(session.Stop(), "A blocked WMI inventory must not prevent export.");
                    Assert(watch.ElapsedMilliseconds < 1500, "Stop waited for blocked WMI inventory.");
                    Assert(Count(ReadRows(session.LogPath), "driver-inventory") == 0, "Late cancelled driver inventory was logged.");
                }
                finally { release.Set(); Assert(left.WaitOne(3000), "Blocked-inventory fixture did not release."); }
            }
        }
    }

    private static void CheckSummary(List<Dictionary<string, object>> rows, DiagnosticSession session)
    {
        Assert(Kind(rows[rows.Count - 1]) == "session-stop", "Summary must be the final complete JSONL line.");
        var footer = Value(rows[rows.Count - 1]);
        long written = Convert.ToInt64(footer["eventsWritten"]);
        long queued = Convert.ToInt64(footer["eventsQueued"]);
        long dropped = Convert.ToInt64(footer["eventsDropped"]);
        long overflows = Convert.ToInt64(footer["queueOverflowEvents"]);
        long discarded = Convert.ToInt64(footer["discardedEvents"]);
        long unchanged = Convert.ToInt64(footer["unchangedAudioObservations"]);
        Assert(written == rows.Count - 2 && written == session.EventCount, "Written-event summary differs from complete event lines.");
        Assert(dropped == overflows + discarded && dropped == session.DroppedEventCount, "Drop counters do not balance.");
        Assert(queued == written + unchanged + discarded, "Accepted queue events do not balance with written/suppressed/discarded events.");
        long total = 0;
        foreach (var pair in (Dictionary<string, object>)footer["counts"]) total += Convert.ToInt64(pair.Value);
        Assert(total == written, "Per-kind counts do not match the event count.");
    }

    private static List<Dictionary<string, object>> ReadRows(string path)
    {
        var result = new List<Dictionary<string, object>>();
        foreach (string line in File.ReadAllLines(path)) result.Add(Json.Deserialize<Dictionary<string, object>>(line));
        return result;
    }
    private static string Kind(Dictionary<string, object> row) { return (string)row["kind"]; }
    private static Dictionary<string, object> Value(Dictionary<string, object> row) { return (Dictionary<string, object>)row["value"]; }
    private static int Count(List<Dictionary<string, object>> rows, string kind) { int count = 0; foreach (var row in rows) if (Kind(row) == kind) count++; return count; }
    private static Dictionary<string, object> Find(List<Dictionary<string, object>> rows, string kind) { foreach (var row in rows) if (Kind(row) == kind) return row; throw new InvalidOperationException("Missing " + kind); }
    private static Dictionary<string, object> FindLast(List<Dictionary<string, object>> rows, string kind) { for (int index = rows.Count - 1; index >= 0; index--) if (Kind(rows[index]) == kind) return rows[index]; throw new InvalidOperationException("Missing " + kind); }
    private static void Wait(Func<bool> predicate, int milliseconds, string message) { var elapsed = Stopwatch.StartNew(); while (!predicate() && elapsed.ElapsedMilliseconds < milliseconds) Thread.Sleep(10); Assert(predicate(), message); }
    private static void Assert(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
}
