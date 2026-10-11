using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AirPodsMicBridge
{
    // Only consumer/telephony HID reports may contain payload bytes. Raw keyboard
    // and mouse input are deliberately outside this diagnostic session.
    internal sealed class DiagnosticRawInput
    {
        internal string Kind { get; set; }
        internal string DeviceName { get; set; }
        internal string DeviceType { get; set; }
        internal int? VendorId { get; set; }
        internal int? ProductId { get; set; }
        internal int? UsagePage { get; set; }
        internal int? Usage { get; set; }
        internal int ReportSize { get; set; }
        internal int ReportCount { get; set; }
        internal string Hex { get; set; }
        internal string Error { get; set; }
    }

    internal sealed class DiagnosticSessionOptions
    {
        internal string DataDirectory;
        internal long MaximumBytes = 10 * 1024 * 1024;
        internal int MaximumSeconds = 600;
        internal int MaximumQueuedEvents = 1024;
        internal int PollMilliseconds = 350;
        internal Func<AudioStateSnapshot> ReadAudioState;
        internal Func<DiagnosticInventoryResult> ReadInventory;
    }

    // Diagnostic capture does not arm control, change mute or open an audio stream.
    // A separate writer drains keyboard/HID events; a blocked audio COM read cannot
    // hold up Stop's bounded writer wait. Construct a new instance for each session.
    internal sealed class DiagnosticSession : IDisposable
    {
        private const int FooterReserveBytes = 4096;
        private readonly object gate = new object();
        private readonly Queue<QueuedEvent> queue = new Queue<QueuedEvent>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly DiagnosticSessionOptions options;
        private readonly string endpointId, endpointName;
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
        private Thread writerThread, pollThread, inventoryThread;
        private StreamWriter writer;
        private bool started, stopRequested, completed, disposed;
        private string logPath, error, stopReason;
        private long eventsWritten, eventsQueued, eventsDropped, queueOverflows, discardedEvents, unchangedAudio;
        private long bytesWritten;

        private sealed class QueuedEvent
        {
            internal string Utc, Kind;
            internal object Value;
        }

        internal DiagnosticSession(string selectedEndpointId, string selectedEndpointName)
            : this(selectedEndpointId, selectedEndpointName, new DiagnosticSessionOptions()) { }

        // Reader and output overrides permit tests without hardware, recordings or mute changes.
        internal DiagnosticSession(string selectedEndpointId, string selectedEndpointName, DiagnosticSessionOptions sessionOptions)
        {
            if (String.IsNullOrWhiteSpace(selectedEndpointId))
                throw new ArgumentException("A capture endpoint ID is required.", "selectedEndpointId");
            if (sessionOptions == null) throw new ArgumentNullException("sessionOptions");
            if (sessionOptions.MaximumBytes < FooterReserveBytes + 1024 || sessionOptions.MaximumSeconds < 1 ||
                sessionOptions.MaximumQueuedEvents < 1 || sessionOptions.PollMilliseconds < 10)
                throw new ArgumentOutOfRangeException("sessionOptions");
            endpointId = Limit(selectedEndpointId, 2048);
            endpointName = Limit(selectedEndpointName, 512);
            options = new DiagnosticSessionOptions {
                DataDirectory = String.IsNullOrWhiteSpace(sessionOptions.DataDirectory) ? PreferenceStore.DataDirectory : Path.GetFullPath(sessionOptions.DataDirectory),
                MaximumBytes = sessionOptions.MaximumBytes,
                MaximumSeconds = sessionOptions.MaximumSeconds,
                MaximumQueuedEvents = sessionOptions.MaximumQueuedEvents,
                PollMilliseconds = sessionOptions.PollMilliseconds,
                ReadAudioState = sessionOptions.ReadAudioState ?? (() => CoreAudioAdapter.Read()),
                ReadInventory = sessionOptions.ReadInventory ?? (() => DiagnosticInventory.Capture())
            };
        }

        internal bool IsRunning { get { lock (gate) return started && !stopRequested && !completed; } }
        internal bool IsComplete { get { lock (gate) return completed; } }
        internal string LogPath { get { lock (gate) return logPath; } }
        internal string Error { get { lock (gate) return error; } }
        internal long EventCount { get { return Interlocked.Read(ref eventsWritten); } }
        internal long DroppedEventCount { get { return Interlocked.Read(ref eventsDropped); } }

        internal void Start()
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("DiagnosticSession");
                if (started || stopRequested) throw new InvalidOperationException("A diagnostic session can only start once.");
                string directory = Path.Combine(options.DataDirectory, "diagnostics");
                try
                {
                    Directory.CreateDirectory(directory);
                    logPath = Path.Combine(directory, "session-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) +
                        "-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".jsonl");
                    writer = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                    started = true;
                    elapsed.Start();
                    writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "Diagnostic JSONL writer" };
                    pollThread = new Thread(PollLoop) { IsBackground = true, Name = "Diagnostic capture observer" };
                    pollThread.SetApartmentState(ApartmentState.MTA);
                    inventoryThread = new Thread(InventoryLoop) { IsBackground = true, Name = "Diagnostic driver inventory" };
                    inventoryThread.SetApartmentState(ApartmentState.MTA);
                    writerThread.Start();
                    pollThread.Start();
                    inventoryThread.Start();
                }
                catch (Exception ex)
                {
                    error = ErrorCode(ex);
                    stopRequested = true;
                    stopReason = "start-error";
                    if (writerThread != null && writerThread.IsAlive) wake.Set();
                    else
                    {
                        completed = true;
                        if (writer != null) { writer.Dispose(); writer = null; }
                    }
                    throw;
                }
            }
        }

        // true means the writer has closed and LogPath is ready to export.
        // Never wait for a pending Core Audio COM read on the UI thread.
        internal bool Stop()
        {
            RequestStop("user");
            Thread thread;
            lock (gate) thread = writerThread;
            if (thread != null && Thread.CurrentThread != thread) thread.Join(1000);
            return IsComplete;
        }

        internal void RecordMediaKey(int virtualKey, int scanCode, int flags, bool keyDown)
        {
            if (!IsMediaKey(virtualKey)) return;
            Enqueue("media-key", new { virtualKey, scanCode, flags, keyDown });
        }

        internal void RecordMediaKey(uint virtualKey, uint scanCode, uint flags, bool keyDown)
        {
            if (virtualKey > Int32.MaxValue) return;
            if (!IsMediaKey((int)virtualKey)) return;
            Enqueue("media-key", new { virtualKey, scanCode, flags, keyDown });
        }

        internal void RecordSequenceStep(int step)
        {
            if (step >= 1 && step <= 6) Enqueue("sequence-step", new { step });
        }

        internal void RecordRawInput(DiagnosticRawInput input)
        {
            if (input == null) return;
            string kind = input.Kind;
            if (kind != "report" && kind != "device-attached" && kind != "device-removed" && kind != "registration-error" &&
                kind != "device-inventory" && kind != "raw-input-error" && kind != "unregistration-error") return;
            bool hid = String.Equals(input.DeviceType, "HID", StringComparison.OrdinalIgnoreCase);
            bool allowedPage = input.UsagePage == 0x0C || input.UsagePage == 0x0B;
            // Even a faulty caller cannot put ordinary raw keys or mouse reports in the log.
            if (kind == "report" && !hid) return;
            string hex = kind == "report" && hid && allowedPage ? NormalizeHex(input.Hex) : null;
            Enqueue("raw-input", new {
                kind,
                deviceName = Limit(input.DeviceName, 1024),
                deviceType = Limit(input.DeviceType, 24),
                vendorId = input.VendorId,
                productId = input.ProductId,
                usagePage = input.UsagePage,
                usage = input.Usage,
                reportSize = Math.Max(0, input.ReportSize),
                reportCount = Math.Max(0, input.ReportCount),
                hex,
                payloadOmitted = !String.IsNullOrEmpty(input.Hex) && hex == null,
                error = Limit(input.Error, 512)
            });
        }

        // Inventory collection happens on a different worker. This method accepts
        // only its fixed driver fields, never serializes arbitrary caller objects.
        internal void RecordInventory(DiagnosticInventoryResult inventory)
        {
            if (inventory == null) return;
            var drivers = new List<Dictionary<string, object>>();
            if (inventory.Drivers != null)
            {
                foreach (var source in inventory.Drivers)
                {
                    if (drivers.Count >= 128) break;
                    if (source == null) continue;
                    var row = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (string field in new[] { "DeviceName", "Manufacturer", "DriverProviderName", "DriverVersion", "DriverDate", "DeviceID", "InfName", "DeviceClass", "IsSigned" })
                    {
                        object value;
                        if (!source.TryGetValue(field, out value) || value == null) continue;
                        if (value is string) row[field] = Limit((string)value, 1024);
                        else if (value is bool || value is int || value is long) row[field] = value;
                    }
                    drivers.Add(row);
                }
            }
            Enqueue("driver-inventory", new { drivers, error = Limit(inventory.Error, 512), truncated = inventory.Truncated });
        }

        private static bool IsMediaKey(int key)
        {
            return key == 0xAD || key == 0xAE || key == 0xAF || (key >= 0xB0 && key <= 0xB3);
        }

        private void Enqueue(string kind, object value)
        {
            lock (gate)
            {
                if (!started || stopRequested || completed || disposed) return;
                if (queue.Count >= options.MaximumQueuedEvents)
                {
                    Interlocked.Increment(ref eventsDropped);
                    queueOverflows++;
                    return;
                }
                queue.Enqueue(new QueuedEvent { Utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Kind = kind, Value = value });
                eventsQueued++;
                wake.Set();
            }
        }

        private void RequestStop(string reason)
        {
            lock (gate)
            {
                if (stopRequested) return;
                stopRequested = true;
                stopReason = reason;
                if (started && !completed) wake.Set();
                else completed = true;
            }
        }

        private void PollLoop()
        {
            while (IsRunning)
            {
                try
                {
                    AudioStateSnapshot state = options.ReadAudioState();
                    if (!IsRunning) break;
                    Enqueue("audio-state", ProjectState(state));
                }
                catch (Exception ex) { Enqueue("audio-read-error", new { error = ErrorCode(ex) }); }
                if (!IsRunning) break;
                Thread.Sleep(options.PollMilliseconds);
            }
        }

        private void InventoryLoop()
        {
            try
            {
                if (IsRunning) RecordInventory(options.ReadInventory());
            }
            catch (Exception ex) { Enqueue("driver-inventory-error", new { error = ErrorCode(ex) }); }
        }

        private object ProjectState(AudioStateSnapshot state)
        {
            var endpoints = new List<Dictionary<string, object>>();
            Dictionary<string, object> selected = null;
            if (state != null && state.CaptureEndpoints != null)
            {
                foreach (var item in state.CaptureEndpoints)
                {
                    if (item == null) continue;
                    var row = new Dictionary<string, object> {
                        { "endpointId", Limit(item.EndpointId, 2048) },
                        { "name", Limit(item.FriendlyName, 512) },
                        { "isAirPods", item.IsAirPods },
                        { "defaultCommunications", item.DefaultCommunications },
                        { "muted", item.Muted },
                        { "activeSessionCount", item.ActiveSessionCount },
                        { "hasActiveCapture", item.HasActiveCapture },
                        { "error", Limit(item.Error, 512) }
                    };
                    endpoints.Add(row);
                    if (String.Equals(item.EndpointId, endpointId, StringComparison.Ordinal)) selected = row;
                }
            }
            endpoints.Sort((left, right) => StringComparer.Ordinal.Compare((string)left["endpointId"], (string)right["endpointId"]));
            return new { selectedPresent = selected != null, selected, captureEndpoints = endpoints,
                adapterError = state == null ? "NullAudioSnapshot" : Limit(state.Error, 512) };
        }

        private void WriterLoop()
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            string lastAudio = null, lastAudioKind = null;
            try
            {
                WriteLine(serializer.Serialize(new { utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), kind = "session-start", value = new {
                    schemaVersion = 1,
                    applicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    osVersion = Environment.OSVersion.VersionString,
                    windowsUiLanguage = CultureInfo.InstalledUICulture.Name,
                    processArchitecture = Environment.Is64BitProcess ? "x64" : "x86",
                    selectedEndpointId = endpointId,
                    selectedEndpointName = endpointName,
                    audioRecording = false,
                    muteChanges = false,
                    capabilities = new[] { "capture-endpoint-state", "media-key-events", "consumer-telephony-HID", "driver-inventory" },
                    kernelBluetoothTracing = false,
                    maximumBytes = options.MaximumBytes,
                    maximumSeconds = options.MaximumSeconds,
                    pollMilliseconds = options.PollMilliseconds
                } }), false);
                while (true)
                {
                    if (elapsed.ElapsedMilliseconds >= options.MaximumSeconds * 1000L) RequestStop("duration-limit");
                    QueuedEvent item = null;
                    lock (gate)
                    {
                        if (queue.Count > 0) item = queue.Dequeue();
                        else if (stopRequested) break;
                    }
                    if (item == null) { wake.WaitOne(100); continue; }
                    string payload = serializer.Serialize(item.Value);
                    if (item.Kind == "audio-state" || item.Kind == "audio-read-error")
                    {
                        if (item.Kind == lastAudioKind && payload == lastAudio) { unchangedAudio++; continue; }
                        lastAudioKind = item.Kind;
                        lastAudio = payload;
                    }
                    string line = serializer.Serialize(new { utc = item.Utc, kind = item.Kind, value = item.Value });
                    if (!WriteLine(line, true))
                    {
                        RequestStop("size-limit");
                        lock (gate)
                        {
                            int discarded = 1 + queue.Count;
                            Interlocked.Add(ref eventsDropped, discarded);
                            discardedEvents += discarded;
                            queue.Clear();
                        }
                        break;
                    }
                    Interlocked.Increment(ref eventsWritten);
                    long count; counts.TryGetValue(item.Kind, out count); counts[item.Kind] = count + 1;
                }
                string footer = serializer.Serialize(new { utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), kind = "session-stop", value = new {
                    reason = stopReason ?? "user",
                    elapsedMilliseconds = elapsed.ElapsedMilliseconds,
                    eventsWritten = Interlocked.Read(ref eventsWritten),
                    eventsQueued,
                    eventsDropped = Interlocked.Read(ref eventsDropped),
                    queueOverflowEvents = queueOverflows,
                    discardedEvents,
                    unchangedAudioObservations = unchangedAudio,
                    counts
                } });
                WriteLine(footer, false);
                writer.Flush();
            }
            catch (Exception ex)
            {
                lock (gate) error = ErrorCode(ex);
                RequestStop("writer-error");
            }
            finally
            {
                try { if (writer != null) writer.Dispose(); }
                catch (Exception ex) { lock (gate) error = ErrorCode(ex); }
                lock (gate) { writer = null; stopRequested = true; completed = true; }
                wake.Dispose();
            }
        }

        private bool WriteLine(string line, bool reserveFooter)
        {
            long length = Encoding.UTF8.GetByteCount(line) + 1L;
            long limit = options.MaximumBytes - (reserveFooter ? FooterReserveBytes : 0);
            if (length > limit - bytesWritten) return false;
            writer.Write(line);
            writer.Write('\n');
            bytesWritten += length;
            return true;
        }

        private static string NormalizeHex(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            var result = new StringBuilder(Math.Min(value.Length, 8192));
            foreach (char character in value)
            {
                if (Char.IsWhiteSpace(character)) continue;
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F'))) return null;
                if (result.Length < 8192) result.Append(Char.ToUpperInvariant(character));
            }
            return result.Length % 2 == 0 ? result.ToString() : null;
        }

        private static string Limit(string value, int length) { return value == null || value.Length <= length ? value : value.Substring(0, length); }
        private static string ErrorCode(Exception exception) { return exception.GetType().Name + ":0x" + exception.HResult.ToString("X8", CultureInfo.InvariantCulture); }

        public void Dispose()
        {
            Stop();
            lock (gate)
            {
                disposed = true;
                if (!started) wake.Dispose();
            }
        }
    }
}
