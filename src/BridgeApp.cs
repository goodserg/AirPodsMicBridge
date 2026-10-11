using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Reflection;

[assembly: AssemblyTitle("AirPods — микрофон")]
[assembly: AssemblyProduct("AirPods Mic Bridge")]
[assembly: AssemblyDescription("Переключение микрофона AirPods в Р7-Команда")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace MagicPodsR7Bridge
{
    internal static class Program
    {
        internal const string StopName = @"Local\AirPodsMicBridge.Stop.v1";
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--stop")
            {
                try { using (var signal = EventWaitHandle.OpenExisting(StopName)) signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return;
            }
            bool owner;
            using (var singleton = new Mutex(true, @"Local\AirPodsMicBridge.Singleton.v1", out owner))
            {
                if (!owner) return;
                try
                {
                    var temp = @"D:\codex\tmp\AirPodsMicBridge";
                    Directory.CreateDirectory(temp);
                    Environment.SetEnvironmentVariable("TEMP", temp);
                    Environment.SetEnvironmentVariable("TMP", temp);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new BridgeContext(temp));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось запустить AirPods Mic Bridge.\n" + ex.Message,
                        "AirPods Mic Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { singleton.ReleaseMutex(); }
            }
        }
    }

    internal sealed class CachedAudio
    {
        internal readonly AudioStateSnapshot State;
        internal readonly long CheckedAt;
        internal CachedAudio(AudioStateSnapshot state) { State = state; CheckedAt = Stopwatch.GetTimestamp(); }
        internal bool Fresh { get { return Elapsed(CheckedAt) < 1200; } }
        internal static double Elapsed(long start)
        {
            return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }
    }

    internal sealed class Gesture
    {
        internal readonly string EndpointId;
        internal readonly long CreatedAt;
        internal readonly int Generation;
        internal readonly bool Injected;
        internal readonly bool? ExpectedMuted;
        internal Gesture(string endpointId, int generation, bool injected, bool? muted)
        {
            EndpointId = endpointId; Generation = generation; Injected = injected; ExpectedMuted = muted; CreatedAt = Stopwatch.GetTimestamp();
        }
    }

    internal sealed class FeedbackNotice
    {
        internal bool? Muted;
        internal string Message;
    }

    internal sealed class MeterReading
    {
        internal readonly string EndpointId;
        internal readonly float? Peak;
        internal readonly long SampledAt = Stopwatch.GetTimestamp();
        internal MeterReading(string endpointId, float? peak) { EndpointId = endpointId; Peak = peak; }
    }

    public sealed class BridgePreferences
    {
        public bool ShowNotificationCard { get; set; }
    }

    internal static class PreferenceStore
    {
        internal static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AirPodsMicBridge", "settings.json");

        internal static BridgePreferences Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return new JavaScriptSerializer().Deserialize<BridgePreferences>(File.ReadAllText(SettingsPath)) ?? new BridgePreferences();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return new BridgePreferences();
        }

        internal static void Save(BridgePreferences preferences)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, new JavaScriptSerializer().Serialize(preferences), new System.Text.UTF8Encoding(false));
        }
    }

    internal sealed class BridgeContext : ApplicationContext
    {
        private const int WmKeyDown = 0x100, WmKeyUp = 0x101, WmSysKeyDown = 0x104, WmSysKeyUp = 0x105;
        private readonly ConcurrentQueue<Gesture> gestures = new ConcurrentQueue<Gesture>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly EventWaitHandle externalStop;
        private readonly Thread audioWorker;
        private readonly NotifyIcon tray;
        private readonly UiFeedback feedback;
        private readonly BridgePreferences preferences;
        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem popupItem;
        private readonly ToolStripMenuItem stateItem;
        private readonly System.Windows.Forms.Timer uiTimer;
        private readonly Icon readyIcon, mutedIcon, waitingIcon;
        private readonly Icon[] levelIcons = new Icon[9];
        private readonly StreamWriter log;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly object logLock = new object();
        private readonly HookProc hookProc;
        private IntPtr hook;
        private volatile CachedAudio cached;
        private volatile MeterReading cachedMeter;
        private volatile bool enabled = true, stopping;
        private int generation;
        private int pending;
        private long lastAccepted;
        private long lastPhysicalDown;
        private bool injectedSuppressed, physicalHeld, physicalSuppressed;
        private string reportedState;
        private string lastTooltip;
        private string observedEndpointId;
        private Icon displayedIcon;
        private float displayedLevel;
        private readonly ConcurrentQueue<FeedbackNotice> notices = new ConcurrentQueue<FeedbackNotice>();

        internal BridgeContext(string logDirectory)
        {
            string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id;
            log = new StreamWriter(Path.Combine(logDirectory, run + ".jsonl"), false, new System.Text.UTF8Encoding(false));
            log.AutoFlush = true;
            externalStop = new EventWaitHandle(false, EventResetMode.ManualReset, Program.StopName);
            externalStop.Reset();
            readyIcon = UiFeedback.CreateTrayIcon(Color.FromArgb(194, 148, 143));
            mutedIcon = UiFeedback.CreateTrayIcon(Color.FromArgb(255, 36, 69));
            waitingIcon = UiFeedback.CreateTrayIcon(Color.Gray);
            for (int i = 0; i < levelIcons.Length; i++) levelIcons[i] = UiFeedback.CreateLevelTrayIcon(i / 8.0f);
            feedback = new UiFeedback();
            preferences = PreferenceStore.Load();
            feedback.ShowNotificationCard = preferences.ShowNotificationCard;
            var menu = new ContextMenuStrip();
            stateItem = new ToolStripMenuItem("Ожидание микрофона Р7") { Enabled = false };
            enabledItem = new ToolStripMenuItem("Управление микрофоном включено") { Checked = true, CheckOnClick = true };
            enabledItem.CheckedChanged += delegate
            {
                enabled = enabledItem.Checked;
                Interlocked.Increment(ref generation);
                wake.Set();
                UpdateTray();
            };
            menu.Items.Add(stateItem);
            menu.Items.Add(enabledItem);
            popupItem = new ToolStripMenuItem("Показывать всплывающую карточку")
            {
                Checked = preferences.ShowNotificationCard, CheckOnClick = true
            };
            popupItem.CheckedChanged += delegate
            {
                preferences.ShowNotificationCard = popupItem.Checked;
                feedback.ShowNotificationCard = popupItem.Checked;
                try { PreferenceStore.Save(preferences); }
                catch (Exception ex) { WriteLog("settings-error", new { message = ex.Message }); }
            };
            menu.Items.Add(popupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = waitingIcon, Text = "AirPods: ожидание микрофона Р7", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate
            {
                UpdateTray();
                feedback.ShowMessage(stateItem.Text);
            };
            hookProc = HandleKey;
            using (var current = Process.GetCurrentProcess())
            using (var module = current.MainModule)
                hook = SetWindowsHookEx(13, hookProc, GetModuleHandle(module.ModuleName), 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            audioWorker = new Thread(AudioLoop) { IsBackground = true, Name = "AirPods microphone monitor" };
            audioWorker.SetApartmentState(ApartmentState.MTA);
            audioWorker.Start();
            uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
            uiTimer.Tick += delegate
            {
                if (externalStop.WaitOne(0)) { ExitThread(); return; }
                UpdateTray();
                FeedbackNotice notice;
                while (notices.TryDequeue(out notice))
                {
                    if (notice.Muted.HasValue) feedback.ShowState(notice.Muted.Value);
                    else feedback.ShowMessage(notice.Message);
                }
            };
            uiTimer.Start();
        }

        private IntPtr HandleKey(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !stopping)
            {
                var data = (KeyboardData)Marshal.PtrToStructure(lParam, typeof(KeyboardData));
                if (data.VkCode == 0xB3)
                {
                    bool injected = (data.Flags & 0x10) != 0;
                    int kind = wParam.ToInt32();
                    if (kind == WmKeyUp || kind == WmSysKeyUp)
                    {
                        bool suppress = injected ? injectedSuppressed : physicalSuppressed;
                        if (injected) injectedSuppressed = false;
                        else { physicalHeld = false; physicalSuppressed = false; }
                        if (suppress) return new IntPtr(1);
                    }
                    else if (kind == WmKeyDown || kind == WmSysKeyDown)
                    {
                        if (!injected)
                        {
                            // Real key repeats keep updating this timestamp; a source with no keyup can recover after silence.
                            bool repeat = physicalHeld && CachedAudio.Elapsed(lastPhysicalDown) < 1500;
                            lastPhysicalDown = Stopwatch.GetTimestamp();
                            if (repeat)
                                return physicalSuppressed ? new IntPtr(1) : CallNextHookEx(hook, code, wParam, lParam);
                            physicalHeld = true;
                        }
                        CachedAudio snapshot = cached;
                        if (enabled && snapshot != null && snapshot.Fresh && snapshot.State.CanArm)
                        {
                            // The observed injected command has no keyup. Time-based debounce must expire.
                            if (lastAccepted != 0 && CachedAudio.Elapsed(lastAccepted) < 400)
                            {
                                if (injected) injectedSuppressed = true; else physicalSuppressed = true;
                                return new IntPtr(1);
                            }
                            if (Interlocked.CompareExchange(ref pending, 1, 0) == 0)
                            {
                                gestures.Enqueue(new Gesture(snapshot.State.EndpointId, generation, injected, snapshot.State.EndpointMuted));
                                lastAccepted = Stopwatch.GetTimestamp();
                                if (injected) injectedSuppressed = true; else physicalSuppressed = true;
                                wake.Set();
                                return new IntPtr(1);
                            }
                        }
                        if (injected) injectedSuppressed = false; else physicalSuppressed = false;
                    }
                }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        private void AudioLoop()
        {
            WriteLog("started", new { version = "1.1.1", processId = Process.GetCurrentProcess().Id });
            CapturePeakMeter meter = null;
            string measuredEndpointId = null;
            long lastStateRead = 0;
            long lastMeterOpen = 0;
            try
            {
                while (!stopping)
                {
                    if (lastStateRead == 0 || CachedAudio.Elapsed(lastStateRead) >= 350 || Volatile.Read(ref pending) != 0)
                    {
                        AudioStateSnapshot state = CoreAudioAdapter.Read();
                        lastStateRead = Stopwatch.GetTimestamp();
                        CachedAudio previous = cached;
                        if (enabled && previous != null && previous.State.CanArm && state.CanArm &&
                            previous.State.EndpointId == state.EndpointId && previous.State.EndpointMuted.HasValue &&
                            state.EndpointMuted.HasValue && previous.State.EndpointMuted != state.EndpointMuted)
                        {
                            // Native headset mute and Fn+F7 can update Core Audio without a Play/Pause key.
                            WriteLog("external-mute", new { endpointId = state.EndpointId, muted = state.EndpointMuted });
                            notices.Enqueue(new FeedbackNotice { Muted = state.EndpointMuted });
                        }
                        cached = new CachedAudio(state);
                        string fingerprint = serializer.Serialize(new { enabled, state });
                        if (fingerprint != reportedState)
                        {
                            reportedState = fingerprint;
                            WriteLog("state", new { enabled, state });
                        }
                        Gesture action;
                        if (gestures.TryDequeue(out action))
                        {
                            try
                            {
                                // Re-read the selected device; never switch a different microphone after a race.
                                AudioStateSnapshot verified = CoreAudioAdapter.Read(action.EndpointId);
                                if (!stopping && enabled && action.Generation == generation &&
                                    CachedAudio.Elapsed(action.CreatedAt) < 1200 && verified.CanArm &&
                                    verified.EndpointId == state.EndpointId && state.CanArm)
                                {
                                    // If the headset already applied native mute, preserve that change instead of undoing it.
                                    if (verified.EndpointMuted != action.ExpectedMuted)
                                    {
                                        WriteLog("native-mute-preserved", new { endpointId = action.EndpointId, muted = verified.EndpointMuted });
                                        if (verified.EndpointMuted.HasValue) notices.Enqueue(new FeedbackNotice { Muted = verified.EndpointMuted });
                                        cached = new CachedAudio(verified);
                                        continue;
                                    }
                                    bool muted = CoreAudioAdapter.Toggle(action.EndpointId);
                                    WriteLog("toggle", new { endpointId = action.EndpointId, before = verified.EndpointMuted, muted, injected = action.Injected });
                                    notices.Enqueue(new FeedbackNotice { Muted = muted });
                                    cached = new CachedAudio(CoreAudioAdapter.Read());
                                }
                                else WriteLog("cancelled", new { endpointId = action.EndpointId, reason = verified.Error ?? "MicrophoneUseChanged" });
                            }
                            catch (Exception ex)
                            {
                                WriteLog("error", new { message = ex.Message, hresult = ex.HResult });
                                notices.Enqueue(new FeedbackNotice { Message = "Не удалось переключить микрофон" });
                                cached = null;
                            }
                            finally { Interlocked.Exchange(ref pending, 0); }
                        }
                    }
                    CachedAudio current = cached;
                    CaptureEndpointSnapshot microphone = current != null && current.Fresh ?
                        ChooseObservedMicrophone(current.State, measuredEndpointId) : null;
                    string endpointId = microphone == null ? null : microphone.EndpointId;
                    if (endpointId != measuredEndpointId || (endpointId != null &&
                        (meter == null || !meter.IsAvailable) && CachedAudio.Elapsed(lastMeterOpen) >= 3000))
                    {
                        if (meter != null) { meter.Dispose(); meter = null; }
                        measuredEndpointId = endpointId;
                        lastMeterOpen = Stopwatch.GetTimestamp();
                        if (endpointId != null) meter = new CapturePeakMeter(endpointId);
                    }
                    float? peak = microphone != null && microphone.Muted == false && meter != null ? meter.ReadPeak() : null;
                    cachedMeter = new MeterReading(measuredEndpointId, peak);
                    wake.WaitOne(100);
                }
            }
            finally { if (meter != null) meter.Dispose(); }
            WriteLog("stopped", new { });
            lock (logLock) log.Dispose();
        }

        private void WriteLog(string kind, object value)
        {
            try { lock (logLock) log.WriteLine(serializer.Serialize(new { utc = DateTime.UtcNow.ToString("o"), kind, value })); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        private static CaptureEndpointSnapshot ChooseObservedMicrophone(AudioStateSnapshot state, string preferredId)
        {
            if (state.Target != null && state.Target.IsAirPods)
                return state.Target;
            CaptureEndpointSnapshot only = null;
            int count = 0;
            foreach (var endpoint in state.CaptureEndpoints ?? new CaptureEndpointSnapshot[0])
            {
                if (!endpoint.IsAirPods) continue;
                if (endpoint.EndpointId == preferredId) return endpoint;
                only = endpoint;
                count++;
            }
            if (count == 1) return only;
            return null;
        }

        private void UpdateTray()
        {
            CachedAudio value = cached;
            bool available = value != null && value.Fresh && value.State.CanArm;
            CaptureEndpointSnapshot microphone = value != null && value.Fresh ?
                ChooseObservedMicrophone(value.State, observedEndpointId) : null;
            if (microphone != null) observedEndpointId = microphone.EndpointId;
            bool? muted = microphone == null ? null : microphone.Muted;
            feedback.UpdateIndicator(muted);
            string text;
            Icon icon;
            if (muted == true) { text = "AirPods: микрофон выключен"; icon = mutedIcon; }
            else if (!enabled) { text = "AirPods: управление приостановлено"; icon = waitingIcon; }
            else if (!muted.HasValue) { text = "AirPods: ожидание подключения"; icon = waitingIcon; }
            else
            {
                text = "AirPods: микрофон включён";
                MeterReading reading = cachedMeter;
                float level = 0;
                bool measured = reading != null && microphone != null && reading.EndpointId == microphone.EndpointId &&
                    CachedAudio.Elapsed(reading.SampledAt) < 500 && reading.Peak.HasValue;
                if (measured && reading.Peak.Value > 0)
                {
                    double decibels = 20 * Math.Log10(Math.Max(0.000001, reading.Peak.Value));
                    level = (float)Math.Max(0, Math.Min(1, (decibels + 60) / 54));
                }
                displayedLevel += (level - displayedLevel) * (level > displayedLevel ? 0.65f : 0.22f);
                icon = measured ? levelIcons[(int)Math.Round(displayedLevel * 8)] : readyIcon;
            }
            if (muted != false || !enabled) displayedLevel = 0;
            stateItem.Text = text;
            if (!available) stateItem.Text += " · ожидание Р7";
            if (lastTooltip != text) { lastTooltip = text; tray.Text = text; }
            if (displayedIcon != icon) { displayedIcon = icon; tray.Icon = icon; }
        }

        protected override void ExitThreadCore()
        {
            if (stopping) return;
            stopping = true;
            enabled = false;
            Interlocked.Increment(ref generation);
            uiTimer.Stop();
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            wake.Set();
            audioWorker.Join(1500);
            tray.Visible = false;
            tray.Dispose();
            feedback.Dispose();
            readyIcon.Dispose(); mutedIcon.Dispose(); waitingIcon.Dispose();
            foreach (Icon icon in levelIcons) icon.Dispose();
            uiTimer.Dispose();
            externalStop.Dispose();
            // Exiting does not unmute the user's microphone.
            base.ExitThreadCore();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardData { internal uint VkCode, ScanCode, Flags, Time; internal UIntPtr ExtraInfo; }
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string name);
    }
}
