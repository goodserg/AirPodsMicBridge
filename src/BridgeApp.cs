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

[assembly: AssemblyTitle("AirPods Mic Bridge")]
[assembly: AssemblyProduct("AirPods Mic Bridge")]
[assembly: AssemblyDescription("AirPods microphone control for Windows applications")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace AirPodsMicBridge
{
    internal static class Program
    {
        internal static readonly string StopName = ScopedName(@"Local\AirPodsMicBridge.Stop.v1");
        internal static readonly string SingletonName = ScopedName(@"Local\AirPodsMicBridge.Singleton.v1");
        internal static readonly string DiagnosticName = ScopedName(@"Local\AirPodsMicBridge.Diagnostics.v1");

        private static string ScopedName(string name)
        {
            if (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIRPODS_MIC_BRIDGE_DATA"))) return name;
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return name + "." + BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(
                    PreferenceStore.DataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant()))).Replace("-", "");
        }
        [STAThread]
        private static int Main(string[] args)
        {
            bool openDiagnostics = args.Length == 1 && args[0] == "--diagnostics";
            if (openDiagnostics)
            {
                try { using (var signal = EventWaitHandle.OpenExisting(DiagnosticName)) signal.Set(); return 0; }
                catch (WaitHandleCannotBeOpenedException) { }
            }
            if (args.Length == 1 && (args[0] == "--startup-enable" || args[0] == "--startup-disable" ||
                args[0] == "--startup-enable-all-users" || args[0] == "--startup-disable-all-users"))
            {
                try
                {
                    if (args[0].EndsWith("-all-users", StringComparison.Ordinal))
                        StartupRegistration.SetEnabledForAllUsers(args[0] == "--startup-enable-all-users", Application.ExecutablePath);
                    else StartupRegistration.SetEnabled(args[0] == "--startup-enable", Application.ExecutablePath);
                    return 0;
                }
                catch { return 1; }
            }
            if (args.Length == 1 && args[0] == "--stop")
            {
                try { using (var signal = EventWaitHandle.OpenExisting(StopName)) signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                var until = Stopwatch.StartNew();
                while (until.ElapsedMilliseconds < 10000)
                {
                    try { using (Mutex.OpenExisting(SingletonName)) { } }
                    catch (WaitHandleCannotBeOpenedException) { return 0; }
                    Thread.Sleep(50);
                }
                return 1;
            }
            if (args.Length == 1 && args[0] == "--autostart" && !StartupRegistration.AutoStartAllowed) return 0;
            bool owner;
            using (var singleton = new Mutex(true, SingletonName, out owner))
            {
                if (!owner) return 0;
                try
                {
                    Localization.SetLanguage(PreferenceStore.Load().Language);
                    var logs = Path.Combine(PreferenceStore.DataDirectory, "logs");
                    Directory.CreateDirectory(logs);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new BridgeContext(logs, openDiagnostics));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Localization.Get("startupError", ex.Message),
                        "AirPods Mic Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { singleton.ReleaseMutex(); }
            }
            return 0;
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
        public string Language { get; set; }
        public string Theme { get; set; }

        public BridgePreferences() { Language = "auto"; Theme = "auto"; }
    }

    internal static class PreferenceStore
    {
        internal static readonly string DataDirectory = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIRPODS_MIC_BRIDGE_DATA")) ?
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirPodsMicBridge") :
            Path.GetFullPath(Environment.GetEnvironmentVariable("AIRPODS_MIC_BRIDGE_DATA"));
        internal static readonly string SettingsPath = Path.Combine(DataDirectory, "settings.json");

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
        private readonly EventWaitHandle externalDiagnostics;
        private readonly Thread audioWorker;
        private readonly NotifyIcon tray;
        private readonly UiFeedback feedback;
        private readonly BridgePreferences preferences;
        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem popupItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem diagnosticItem;
        private DiagnosticForm diagnosticForm;
        private readonly ToolStripMenuItem stateItem;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem languageItem, themeItem, exitItem;
        private readonly System.Windows.Forms.Timer uiTimer;
        private Icon readyIcon, mutedIcon, waitingIcon;
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
        private long lastAppearanceCheck;
        private readonly ConcurrentQueue<FeedbackNotice> notices = new ConcurrentQueue<FeedbackNotice>();

        internal BridgeContext(string logDirectory, bool openDiagnostics = false)
        {
            string run = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id;
            log = new StreamWriter(Path.Combine(logDirectory, run + ".jsonl"), false, new System.Text.UTF8Encoding(false));
            log.AutoFlush = true;
            externalStop = new EventWaitHandle(false, EventResetMode.ManualReset, Program.StopName);
            externalStop.Reset();
            externalDiagnostics = new EventWaitHandle(openDiagnostics, EventResetMode.AutoReset, Program.DiagnosticName);
            preferences = PreferenceStore.Load();
            Localization.SetLanguage(preferences.Language);
            Appearance.SetTheme(Appearance.ResolveTheme(preferences.Theme));
            Appearance.SetTrayTheme(Appearance.ResolveTrayTheme());
            ReplaceTrayIcons();
            feedback = new UiFeedback();
            feedback.ShowNotificationCard = preferences.ShowNotificationCard;
            menu = new ContextMenuStrip();
            stateItem = new ToolStripMenuItem(Localization.Get("waitingMicrophone")) { Enabled = false };
            enabledItem = new ToolStripMenuItem(Localization.Get("controlEnabled")) { Checked = true, CheckOnClick = true };
            enabledItem.CheckedChanged += delegate
            {
                enabled = enabledItem.Checked;
                Interlocked.Increment(ref generation);
                wake.Set();
                UpdateTray();
            };
            menu.Items.Add(stateItem);
            menu.Items.Add(enabledItem);
            popupItem = new ToolStripMenuItem(Localization.Get("showCard"))
            {
                Checked = preferences.ShowNotificationCard, CheckOnClick = true
            };
            popupItem.CheckedChanged += delegate
            {
                preferences.ShowNotificationCard = popupItem.Checked;
                feedback.ShowNotificationCard = popupItem.Checked;
                SavePreferences();
            };
            menu.Items.Add(popupItem);
            startupItem = new ToolStripMenuItem(Localization.Get("startWithWindows")) { Checked = StartupRegistration.IsEnabled };
            startupItem.Click += delegate
            {
                try
                {
                    StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled, Application.ExecutablePath);
                    startupItem.Checked = StartupRegistration.IsEnabled;
                }
                catch (Exception ex)
                {
                    WriteLog("startup-setting-error", new { message = ex.Message });
                    feedback.ShowMessage(Localization.Get("startupSettingFailed"));
                }
            };
            menu.Items.Add(startupItem);
            diagnosticItem = new ToolStripMenuItem(Localization.Get("diagnosticTitle"), null, delegate { OpenDiagnostics(); });
            menu.Items.Add(diagnosticItem);
            menu.Opening += delegate { startupItem.Checked = StartupRegistration.IsEnabled; };
            menu.Items.Add(new ToolStripSeparator());
            themeItem = new ToolStripMenuItem();
            languageItem = new ToolStripMenuItem();
            AddChoice(themeItem, "auto", null, delegate { SelectTheme("auto"); });
            AddChoice(themeItem, "light", null, delegate { SelectTheme("light"); });
            AddChoice(themeItem, "dark", null, delegate { SelectTheme("dark"); });
            AddChoice(languageItem, "auto", null, delegate { SelectLanguage("auto"); });
            foreach (LanguageOption option in Localization.Languages)
            {
                string code = option.Code;
                AddChoice(languageItem, code, option.NativeName, delegate { SelectLanguage(code); });
            }
            menu.Items.Add(themeItem);
            menu.Items.Add(languageItem);
            menu.Items.Add(new ToolStripSeparator());
            exitItem = new ToolStripMenuItem(String.Empty, null, delegate { ExitThread(); });
            menu.Items.Add(exitItem);
            tray = new NotifyIcon { Icon = waitingIcon, Text = Localization.Get("tooltipWaiting"), ContextMenuStrip = menu, Visible = true };
            RefreshMenuText();
            Appearance.ApplyMenu(menu);
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
                if (externalDiagnostics.WaitOne(0)) OpenDiagnostics();
                if (lastAppearanceCheck == 0 || CachedAudio.Elapsed(lastAppearanceCheck) >= 1000)
                {
                    RefreshAppearance();
                    lastAppearanceCheck = Stopwatch.GetTimestamp();
                }
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

        private void OpenDiagnostics()
        {
            if (diagnosticForm != null && !diagnosticForm.IsDisposed)
            {
                ShowWindow(diagnosticForm.Handle, 1);
                diagnosticForm.Activate();
                return;
            }
            diagnosticForm = new DiagnosticForm(delegate(bool active)
            {
                if (stopping) return;
                enabledItem.Enabled = !active;
                enabled = !active && enabledItem.Checked;
                Interlocked.Increment(ref generation);
                wake.Set();
                UpdateTray();
            });
            diagnosticForm.FormClosed += delegate { diagnosticForm = null; };
            diagnosticForm.Show();
            // Background startup may supply SW_HIDE for the process's first window.
            // Opening diagnostics is an explicit request to show this window.
            ShowWindow(diagnosticForm.Handle, 1);
            diagnosticForm.Activate();
        }

        private IntPtr HandleKey(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !stopping)
            {
                var data = (KeyboardData)Marshal.PtrToStructure(lParam, typeof(KeyboardData));
                DiagnosticSession diagnostic = diagnosticForm == null ? null : diagnosticForm.ActiveSession;
                if (diagnostic != null && diagnostic.IsRunning)
                {
                    int message = wParam.ToInt32();
                    if (message == WmKeyDown || message == WmSysKeyDown || message == WmKeyUp || message == WmSysKeyUp)
                        diagnostic.RecordMediaKey(data.VkCode, data.ScanCode, data.Flags, message == WmKeyDown || message == WmSysKeyDown);
                }
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
            WriteLog("started", new { version = "1.3.0", processId = Process.GetCurrentProcess().Id });
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
                                notices.Enqueue(new FeedbackNotice { Message = Localization.Get("toggleFailed") });
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
            if (muted == true) { text = Localization.Get("tooltipMuted"); icon = mutedIcon; }
            else if (!enabled) { text = Localization.Get("tooltipPaused"); icon = waitingIcon; }
            else if (!muted.HasValue) { text = Localization.Get("tooltipWaiting"); icon = waitingIcon; }
            else
            {
                text = Localization.Get("tooltipLive");
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
            string stateText = available ? text : text + Localization.Get("waitingMicrophoneSuffix");
            if (stateItem.Text != stateText) stateItem.Text = stateText;
            if (lastTooltip != text) { lastTooltip = text; tray.Text = text.Length > 63 ? text.Substring(0, 62) + "…" : text; }
            if (displayedIcon != icon) { displayedIcon = icon; tray.Icon = icon; }
        }

        private static void AddChoice(ToolStripMenuItem parent, string code, string text, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text ?? String.Empty) { Tag = code };
            item.Click += handler;
            parent.DropDownItems.Add(item);
        }

        private void SavePreferences()
        {
            try { PreferenceStore.Save(preferences); }
            catch (Exception ex) { WriteLog("settings-error", new { message = ex.Message }); }
        }

        private void SelectLanguage(string code)
        {
            preferences.Language = code;
            Localization.SetLanguage(code);
            SavePreferences();
            RefreshMenuText();
            feedback.UpdateAppearance();
            lastTooltip = null;
            UpdateTray();
        }

        private void SelectTheme(string code)
        {
            preferences.Theme = code;
            SavePreferences();
            RefreshMenuText();
            RefreshAppearance();
        }

        private void RefreshMenuText()
        {
            enabledItem.Text = Localization.Get("controlEnabled");
            startupItem.Text = Localization.Get("startWithWindows");
            diagnosticItem.Text = Localization.Get("diagnosticTitle");
            popupItem.Text = Localization.Get("showCard");
            languageItem.Text = Localization.Get("language");
            themeItem.Text = Localization.Get("theme");
            exitItem.Text = Localization.Get("exit");
            string language = Localization.ResolveLanguage(preferences.Language, System.Globalization.CultureInfo.CurrentUICulture);
            foreach (ToolStripMenuItem item in languageItem.DropDownItems)
            {
                string code = (string)item.Tag;
                if (code == "auto") item.Text = Localization.Get("automatic");
                bool automatic = !Array.Exists(Localization.Languages, option =>
                    String.Equals(option.Code, preferences.Language, StringComparison.OrdinalIgnoreCase));
                item.Checked = automatic ? code == "auto" : code == language;
            }
            foreach (ToolStripMenuItem item in themeItem.DropDownItems)
            {
                string code = (string)item.Tag;
                item.Text = Localization.Get(code == "auto" ? "automatic" : code);
                string selected = preferences.Theme == "light" || preferences.Theme == "dark" ? preferences.Theme : "auto";
                item.Checked = code == selected;
            }
        }

        private void RefreshAppearance()
        {
            AppTheme theme = Appearance.ResolveTheme(preferences.Theme);
            AppTheme trayTheme = Appearance.ResolveTrayTheme();
            if (theme != Appearance.CurrentTheme)
            {
                Appearance.SetTheme(theme);
                Appearance.ApplyMenu(menu);
                feedback.UpdateAppearance();
                if (diagnosticForm != null) diagnosticForm.UpdateAppearance();
            }
            if (trayTheme != Appearance.CurrentTrayTheme)
            {
                Appearance.SetTrayTheme(trayTheme);
                ReplaceTrayIcons();
            }
        }

        // Build the replacement cache before releasing any icon used by NotifyIcon.
        private void ReplaceTrayIcons()
        {
            var replacements = new Icon[12];
            try
            {
                replacements[0] = UiFeedback.CreateTrayIcon(Appearance.TrayOutlineColor);
                replacements[1] = UiFeedback.CreateTrayIcon(Color.FromArgb(255, 36, 69));
                replacements[2] = UiFeedback.CreateTrayIcon(Color.Gray);
                for (int i = 0; i < 9; i++) replacements[3 + i] = UiFeedback.CreateLevelTrayIcon(i / 8.0f);
            }
            catch
            {
                foreach (Icon icon in replacements) if (icon != null) icon.Dispose();
                throw;
            }
            Icon oldReady = readyIcon, oldMuted = mutedIcon, oldWaiting = waitingIcon;
            Icon[] oldLevels = (Icon[])levelIcons.Clone();
            readyIcon = replacements[0]; mutedIcon = replacements[1]; waitingIcon = replacements[2];
            Array.Copy(replacements, 3, levelIcons, 0, 9);
            if (tray != null) { displayedIcon = null; UpdateTray(); }
            if (oldReady != null) oldReady.Dispose();
            if (oldMuted != null) oldMuted.Dispose();
            if (oldWaiting != null) oldWaiting.Dispose();
            foreach (Icon icon in oldLevels) if (icon != null) icon.Dispose();
        }

        protected override void ExitThreadCore()
        {
            if (stopping) return;
            stopping = true;
            if (diagnosticForm != null) diagnosticForm.Close();
            enabled = false;
            Interlocked.Increment(ref generation);
            uiTimer.Stop();
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            wake.Set();
            audioWorker.Join(1500);
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            feedback.Dispose();
            readyIcon.Dispose(); mutedIcon.Dispose(); waitingIcon.Dispose();
            foreach (Icon icon in levelIcons) icon.Dispose();
            uiTimer.Dispose();
            externalStop.Dispose();
            externalDiagnostics.Dispose();
            // Exiting does not unmute the user's microphone.
            base.ExitThreadCore();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardData { internal uint VkCode, ScanCode, Flags, Time; internal UIntPtr ExtraInfo; }
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] private static extern IntPtr GetModuleHandle(string name);
    }
}
