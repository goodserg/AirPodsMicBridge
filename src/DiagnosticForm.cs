using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Appearance = AirPodsMicBridge.Appearance;

namespace AirPodsMicBridge
{
    // Passive diagnostics only: no audio stream, mute operation, or injected input.
    internal sealed class DiagnosticForm : Form
    {
        private readonly Action<bool> activeChanged;
        private readonly ComboBox microphones;
        private readonly Button refreshButton, startButton, stopButton, saveButton, closeButton;
        private readonly Label instructions, microphoneLabel, statusLabel, eventLabel;
        private readonly DiagnosticProgress progress;
        private readonly Timer timer;
        private readonly Stopwatch sequence = new Stopwatch();
        private readonly Dictionary<IntPtr, DiagnosticRawInput> devices = new Dictionary<IntPtr, DiagnosticRawInput>();
        private Icon windowIcon;
        private DiagnosticSession session;
        private int lastStep;
        private bool rawRegistered, notifiedActive, stopping, disposed;
        private AppTheme shownTheme;
        private string shownLanguage;

        internal DiagnosticSession ActiveSession { get { return session != null && session.IsRunning ? session : null; } }

        internal DiagnosticForm(Action<bool> activeChanged)
        {
            this.activeChanged = activeChanged;
            try { windowIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (windowIcon != null) Icon = windowIcon; }
            catch { }
            Text = T("diagnosticTitle");
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 445);
            MinimumSize = new Size(600, 445);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(layout);
            instructions = new Label { Dock = DockStyle.Fill, Text = T("diagnosticInstructions") };
            microphoneLabel = new Label { Dock = DockStyle.Fill, Text = T("diagnosticMicrophone") };
            microphones = new DiagnosticComboBox { Dock = DockStyle.Fill };
            refreshButton = Button(T("diagnosticRefresh"));
            var select = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            select.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            select.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            select.Controls.Add(microphones, 0, 0); select.Controls.Add(refreshButton, 1, 0);
            statusLabel = new Label { Dock = DockStyle.Fill, Text = T("diagnosticReady"), Padding = new Padding(0, 12, 0, 0) };
            progress = new DiagnosticProgress { Dock = DockStyle.Fill, Minimum = 0, Maximum = 30, AccessibleName = T("diagnosticTitle") };
            eventLabel = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            startButton = Button(T("diagnosticStart")); stopButton = Button(T("diagnosticStop"));
            saveButton = Button(T("diagnosticSave")); closeButton = Button(T("diagnosticClose"));
            stopButton.Enabled = false; saveButton.Enabled = false;
            buttons.Controls.Add(startButton); buttons.Controls.Add(stopButton); buttons.Controls.Add(saveButton); buttons.Controls.Add(closeButton);
            layout.Controls.Add(instructions, 0, 0); layout.Controls.Add(microphoneLabel, 0, 1);
            layout.Controls.Add(select, 0, 2); layout.Controls.Add(statusLabel, 0, 3);
            layout.Controls.Add(progress, 0, 4); layout.Controls.Add(eventLabel, 0, 5); layout.Controls.Add(buttons, 0, 6);
            refreshButton.Click += delegate { RefreshMicrophones(); };
            startButton.Click += delegate { StartDiagnostic(); };
            stopButton.Click += delegate { StopDiagnostic(); };
            saveButton.Click += delegate { SaveReport(); };
            closeButton.Click += delegate { Close(); };
            microphones.SelectedIndexChanged += delegate { startButton.Enabled = session == null || !session.IsRunning ? microphones.SelectedItem != null && !stopping : false; };
            timer = new Timer { Interval = 150 };
            timer.Tick += delegate { TickDiagnostic(); };
            Shown += delegate { RefreshMicrophones(); timer.Start(); };
            FormClosing += delegate { StopDiagnostic(); };
            UpdateAppearance();
        }

        private static Button Button(string text)
        {
            return new DiagnosticButton { Text = text, AutoSize = true, MinimumSize = new Size(86, 32), Padding = new Padding(8, 0, 8, 0), Margin = new Padding(0, 0, 8, 0) };
        }
        private static string T(string key)
        {
            try { return Localization.Get(key); }
            catch (KeyNotFoundException) { return key; }
        }

        internal void UpdateAppearance()
        {
            shownTheme = Appearance.CurrentTheme; shownLanguage = Localization.CurrentLanguage;
            BackColor = Appearance.BackgroundColor; ForeColor = Appearance.ForegroundColor;
            ApplyColors(this);
            Text = T("diagnosticTitle"); instructions.Text = T("diagnosticInstructions");
            microphoneLabel.Text = T("diagnosticMicrophone"); refreshButton.Text = T("diagnosticRefresh");
            startButton.Text = T("diagnosticStart"); stopButton.Text = T("diagnosticStop");
            saveButton.Text = T("diagnosticSave"); closeButton.Text = T("diagnosticClose");
            progress.AccessibleName = T("diagnosticTitle");
            ApplyWindowTheme();
            Invalidate(true);
        }
        private static void ApplyColors(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                control.BackColor = Appearance.BackgroundColor; control.ForeColor = Appearance.ForegroundColor;
                Button button = control as Button;
                if (button != null) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Appearance.BorderColor; }
                DiagnosticComboBox combo = control as DiagnosticComboBox;
                if (combo != null) combo.UpdateTheme();
                control.Invalidate();
                ApplyColors(control);
            }
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            ApplyWindowTheme();
        }

        private void ApplyWindowTheme()
        {
            if (!IsHandleCreated) return;
            try
            {
                int dark = Appearance.CurrentTheme == AppTheme.Dark ? 1 : 0;
                int caption = ColorTranslator.ToWin32(Appearance.BackgroundColor);
                int text = ColorTranslator.ToWin32(Appearance.ForegroundColor);
                int border = ColorTranslator.ToWin32(Appearance.BorderColor);
                // Windows 11 supports these documented DWM colors independently of the OS theme.
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(Handle, 36, ref text, 4);
                DwmSetWindowAttribute(Handle, 34, ref border, 4);
            }
            catch { }
        }

        private void RefreshMicrophones()
        {
            if (session != null && session.IsRunning) return;
            string previous = microphones.SelectedItem is EndpointItem ? ((EndpointItem)microphones.SelectedItem).Endpoint.EndpointId : null;
            try
            {
                AudioStateSnapshot state = CoreAudioAdapter.Read();
                microphones.BeginUpdate(); microphones.Items.Clear();
                foreach (CaptureEndpointSnapshot endpoint in state.CaptureEndpoints ?? new CaptureEndpointSnapshot[0])
                    if (endpoint != null && !String.IsNullOrEmpty(endpoint.EndpointId)) microphones.Items.Add(new EndpointItem(endpoint));
                int selected = 0;
                for (int i = 0; i < microphones.Items.Count; i++)
                    if (String.Equals(((EndpointItem)microphones.Items[i]).Endpoint.EndpointId, previous, StringComparison.Ordinal)) selected = i;
                if (microphones.Items.Count > 0) microphones.SelectedIndex = selected;
                startButton.Enabled = microphones.Items.Count > 0 && !stopping;
                if (microphones.Items.Count == 0) statusLabel.Text = T("diagnosticNoMicrophones");
                else if (session == null) statusLabel.Text = T("diagnosticReady");
            }
            catch (Exception ex) { statusLabel.Text = T("diagnosticError") + ": " + ex.Message; startButton.Enabled = false; }
            finally { microphones.EndUpdate(); }
        }

        private void StartDiagnostic()
        {
            EndpointItem selected = microphones.SelectedItem as EndpointItem;
            if (selected == null || stopping || (session != null && session.IsRunning)) return;
            try
            {
                if (session != null) { session.Dispose(); session = null; }
                // Pause the bridge before opening the event logger so no diagnostic gesture can toggle it.
                SetActive(true);
                session = new DiagnosticSession(selected.Endpoint.EndpointId, selected.Endpoint.FriendlyName);
                session.Start();
                if (!session.IsRunning) throw new InvalidOperationException(session.Error ?? "Diagnostic session did not start.");
                devices.Clear();
                RegisterRawInput();
                InventoryDevices();
                microphones.Enabled = false; refreshButton.Enabled = false; startButton.Enabled = false;
                stopButton.Enabled = true; saveButton.Enabled = false; progress.Value = 0;
                stopping = false; lastStep = 0; sequence.Restart();
                TickDiagnostic();
            }
            catch (Exception ex)
            {
                StopDiagnostic();
                statusLabel.Text = T("diagnosticError") + ": " + ex.Message;
            }
        }

        private void TickDiagnostic()
        {
            if (shownTheme != Appearance.CurrentTheme || shownLanguage != Localization.CurrentLanguage) UpdateAppearance();
            if (session == null) return;
            eventLabel.Text = T("diagnosticEvents") + ": " + session.EventCount;
            if (session.IsRunning && !rawRegistered) eventLabel.Text += "\n" + T("diagnosticRawInputUnavailable");
            if (session.IsRunning)
            {
                double elapsed = sequence.Elapsed.TotalSeconds;
                if (elapsed >= 30) { StopDiagnostic(); return; }
                int step = (int)(elapsed / 5) + 1;
                if (step != lastStep) { lastStep = step; session.RecordSequenceStep(step); }
                int remaining = Math.Max(1, (int)Math.Ceiling(step * 5 - elapsed));
                statusLabel.Text = T("diagnosticPress") + " · " + step + " / 6 · " + remaining + " s";
                progress.Value = Math.Min(30, (int)elapsed);
            }
            else if (notifiedActive) StopDiagnostic();
            else if (stopping && session.IsComplete)
            {
                stopping = false; saveButton.Enabled = File.Exists(session.LogPath);
                startButton.Enabled = microphones.SelectedItem != null;
                statusLabel.Text = String.IsNullOrEmpty(session.Error) ? T("diagnosticFinished") : T("diagnosticError") + ": " + session.Error;
            }
        }

        private void StopDiagnostic()
        {
            sequence.Stop();
            UnregisterRawInput();
            try
            {
                if (session != null && !session.IsComplete) stopping = !session.Stop();
                if (session != null && !session.IsRunning && session.IsComplete)
                {
                    stopping = false; saveButton.Enabled = File.Exists(session.LogPath);
                    statusLabel.Text = String.IsNullOrEmpty(session.Error) ? T("diagnosticFinished") : T("diagnosticError") + ": " + session.Error;
                }
            }
            catch (Exception ex) { statusLabel.Text = T("diagnosticError") + ": " + ex.Message; stopping = false; }
            finally
            {
                SetActive(false);
                stopButton.Enabled = false; microphones.Enabled = true; refreshButton.Enabled = true;
                startButton.Enabled = microphones.SelectedItem != null && !stopping;
            }
        }

        private void SetActive(bool active)
        {
            if (notifiedActive == active) return;
            notifiedActive = active;
            if (activeChanged != null) activeChanged(active);
        }

        private void SaveReport()
        {
            if (session == null || !session.IsComplete || !File.Exists(session.LogPath)) return;
            using (var dialog = new SaveFileDialog { Filter = T("diagnosticReportFilter"), DefaultExt = "jsonl", AddExtension = true,
                FileName = "AirPodsMicBridge-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), OverwritePrompt = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (!String.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(session.LogPath), StringComparison.OrdinalIgnoreCase))
                        File.Copy(session.LogPath, dialog.FileName, true);
                    statusLabel.Text = T("diagnosticSaved") + ": " + dialog.FileName;
                }
                catch (Exception ex) { statusLabel.Text = T("diagnosticSaveFailed") + ": " + ex.Message; }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Stop(); StopDiagnostic();
                if (session != null) { session.Dispose(); session = null; }
                timer.Dispose();
            }
            base.Dispose(disposing);
            if (disposing && windowIcon != null) { windowIcon.Dispose(); windowIcon = null; }
        }

        protected override void WndProc(ref Message message)
        {
            // Base processing performs DefWindowProc cleanup for foreground WM_INPUT.
            if (session != null && session.IsRunning)
            {
                try
                {
                    if (message.Msg == 0x00FF) ReadRawInput(message.LParam);
                    else if (message.Msg == 0x00FE) DeviceChanged(message.WParam.ToInt64() == 1, message.LParam);
                }
                catch (Exception ex) { RecordError("raw-input-error", ex.GetType().Name + ":0x" + ex.HResult.ToString("X8")); }
            }
            base.WndProc(ref message);
            if (message.Msg == 0x031A) ApplyWindowTheme();
        }

        private void RegisterRawInput()
        {
            var registrations = Registrations(0x100 | 0x2000, Handle);
            rawRegistered = RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
            if (!rawRegistered)
            {
                RecordError("registration-error", "Win32:" + Marshal.GetLastWin32Error());
                statusLabel.Text = T("diagnosticRawInputUnavailable");
            }
        }
        private void UnregisterRawInput()
        {
            if (!rawRegistered) return;
            var registrations = Registrations(1, IntPtr.Zero);
            if (!RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf(typeof(RawInputDevice))))
                RecordError("unregistration-error", "Win32:" + Marshal.GetLastWin32Error());
            rawRegistered = false; devices.Clear();
        }
        private static RawInputDevice[] Registrations(uint flags, IntPtr window)
        {
            return new[] {
                new RawInputDevice { UsagePage = 0x0C, Usage = 1, Flags = flags, Target = window },
                new RawInputDevice { UsagePage = 0x0B, Usage = 1, Flags = flags, Target = window },
                new RawInputDevice { UsagePage = 0x0B, Usage = 5, Flags = flags, Target = window }
            };
        }

        private void InventoryDevices()
        {
            uint count = 0; uint itemSize = (uint)Marshal.SizeOf(typeof(RawInputDeviceList));
            if (GetRawInputDeviceList(IntPtr.Zero, ref count, itemSize) == UInt32.MaxValue || count > 512) return;
            if (count == 0) return;
            IntPtr list = Marshal.AllocHGlobal(checked((int)(count * itemSize)));
            try
            {
                uint returned = GetRawInputDeviceList(list, ref count, itemSize);
                if (returned == UInt32.MaxValue) return;
                for (uint i = 0; i < Math.Min(returned, count); i++)
                {
                    var item = (RawInputDeviceList)Marshal.PtrToStructure(IntPtr.Add(list, checked((int)(i * itemSize))), typeof(RawInputDeviceList));
                    if (item.Type != 2) continue;
                    DiagnosticRawInput info = DeviceInfo(item.Device);
                    if (info.UsagePage != 0x0B && info.UsagePage != 0x0C) continue;
                    info.Kind = "device-inventory"; session.RecordRawInput(info);
                }
            }
            finally { Marshal.FreeHGlobal(list); }
        }

        private void DeviceChanged(bool attached, IntPtr handle)
        {
            DiagnosticRawInput info;
            if (attached) info = DeviceInfo(handle);
            else if (!devices.TryGetValue(handle, out info)) info = new DiagnosticRawInput { DeviceName = "0x" + handle.ToInt64().ToString("X"), DeviceType = "HID" };
            DiagnosticRawInput change = CopyInfo(info);
            change.Kind = attached ? "device-attached" : "device-removed";
            session.RecordRawInput(change);
            if (!attached) devices.Remove(handle);
        }

        private DiagnosticRawInput DeviceInfo(IntPtr handle)
        {
            DiagnosticRawInput known;
            if (devices.TryGetValue(handle, out known)) return known;
            var info = new DiagnosticRawInput { DeviceType = "HID" };
            uint characters = 0;
            if (GetRawInputDeviceInfo(handle, 0x20000007, IntPtr.Zero, ref characters) != UInt32.MaxValue && characters > 0 && characters <= 8192)
            {
                IntPtr name = Marshal.AllocHGlobal(checked((int)((characters + 1) * 2)));
                try
                {
                    uint capacity = characters + 1;
                    if (GetRawInputDeviceInfo(handle, 0x20000007, name, ref capacity) != UInt32.MaxValue)
                        info.DeviceName = Marshal.PtrToStringUni(name, (int)Math.Min(characters, capacity)).TrimEnd('\0');
                }
                finally { Marshal.FreeHGlobal(name); }
            }
            IntPtr metadata = Marshal.AllocHGlobal(32);
            try
            {
                for (int i = 0; i < 32; i += 4) Marshal.WriteInt32(metadata, i, 0);
                Marshal.WriteInt32(metadata, 0, 32); uint size = 32;
                if (GetRawInputDeviceInfo(handle, 0x2000000B, metadata, ref size) != UInt32.MaxValue && size >= 24 && Marshal.ReadInt32(metadata, 4) == 2)
                {
                    info.VendorId = Marshal.ReadInt32(metadata, 8); info.ProductId = Marshal.ReadInt32(metadata, 12);
                    info.UsagePage = (ushort)Marshal.ReadInt16(metadata, 20); info.Usage = (ushort)Marshal.ReadInt16(metadata, 22);
                }
                else info.Error = "DeviceInfoUnavailable";
            }
            finally { Marshal.FreeHGlobal(metadata); }
            if (String.IsNullOrEmpty(info.DeviceName)) info.DeviceName = "0x" + handle.ToInt64().ToString("X");
            devices[handle] = info;
            return info;
        }

        private void ReadRawInput(IntPtr input)
        {
            uint length = 0; uint headerSize = (uint)(8 + 2 * IntPtr.Size);
            if (GetRawInputData(input, 0x10000003, IntPtr.Zero, ref length, headerSize) == UInt32.MaxValue) return;
            if (length < headerSize + 8 || length > 65536) { RecordError("raw-input-error", "RawInputSizeOutOfRange:" + length); return; }
            IntPtr packet = Marshal.AllocHGlobal((int)length);
            try
            {
                uint copied = GetRawInputData(input, 0x10000003, packet, ref length, headerSize);
                if (copied == UInt32.MaxValue || copied < headerSize + 8 || Marshal.ReadInt32(packet, 0) != 2) return;
                byte[] bytes = new byte[copied]; Marshal.Copy(packet, bytes, 0, bytes.Length);
                byte[] report; int reportSize, reportCount; string error;
                if (!TryParseRawHid(bytes, IntPtr.Size, out report, out reportSize, out reportCount, out error)) { RecordError("raw-input-error", error); return; }
                DiagnosticRawInput info = CopyInfo(DeviceInfo(Marshal.ReadIntPtr(packet, 8)));
                info.Kind = "report"; info.ReportSize = reportSize; info.ReportCount = reportCount;
                // Unknown/vendor HID collections get metadata only; never risk storing keyboard reports.
                if (info.UsagePage == 0x0B || info.UsagePage == 0x0C) info.Hex = BitConverter.ToString(report).Replace("-", "");
                info.Error = error ?? info.Error;
                session.RecordRawInput(info);
            }
            finally { Marshal.FreeHGlobal(packet); }
        }

        // Pure bounds parser used by checks. Header is 16 bytes on x86, 24 bytes on x64.
        internal static bool TryParseRawHid(byte[] packet, int pointerSize, out byte[] report, out int reportSize, out int reportCount, out string error)
        {
            report = null; reportSize = 0; reportCount = 0; error = null;
            if (packet == null || (pointerSize != 4 && pointerSize != 8)) { error = "InvalidRawInputPacket"; return false; }
            int headerSize = 8 + 2 * pointerSize;
            if (packet.Length < headerSize + 8 || BitConverter.ToUInt32(packet, 0) != 2) { error = "NotRawHid"; return false; }
            uint declaredLength = BitConverter.ToUInt32(packet, 4);
            uint size = BitConverter.ToUInt32(packet, headerSize), count = BitConverter.ToUInt32(packet, headerSize + 4);
            ulong payloadSize = (ulong)size * count;
            if (declaredLength > packet.Length || declaredLength < headerSize + 8 || size == 0 || count == 0 ||
                size > Int32.MaxValue || count > Int32.MaxValue || payloadSize > (ulong)(declaredLength - headerSize - 8))
            { error = "InvalidRawHidBounds"; return false; }
            reportSize = (int)size; reportCount = (int)count;
            int copied = (int)Math.Min(payloadSize, 4096UL);
            report = new byte[copied]; Buffer.BlockCopy(packet, headerSize + 8, report, 0, copied);
            if (payloadSize > 4096) error = "ReportDataTruncated";
            return true;
        }
        private static DiagnosticRawInput CopyInfo(DiagnosticRawInput source)
        {
            return new DiagnosticRawInput { DeviceName = source.DeviceName, DeviceType = source.DeviceType,
                VendorId = source.VendorId, ProductId = source.ProductId, UsagePage = source.UsagePage, Usage = source.Usage, Error = source.Error };
        }
        private void RecordError(string kind, string error)
        {
            if (session != null && session.IsRunning) session.RecordRawInput(new DiagnosticRawInput { Kind = kind, Error = error });
        }

        private sealed class EndpointItem
        {
            internal readonly CaptureEndpointSnapshot Endpoint;
            internal EndpointItem(CaptureEndpointSnapshot endpoint) { Endpoint = endpoint; }
            public override string ToString() { return String.IsNullOrEmpty(Endpoint.FriendlyName) ? T("diagnosticMicrophone") : Endpoint.FriendlyName; }
        }
        [StructLayout(LayoutKind.Sequential)] private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] private struct RawInputDeviceList { public IntPtr Device; public uint Type; }
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }

    // Retain the native ComboBox's keyboard/navigation/accessibility behavior. Paint its
    // selection surface and arrow as well as list items so native light chrome cannot leak.
    internal sealed class DiagnosticComboBox : ComboBox
    {
        internal DiagnosticComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            UpdateTheme();
        }

        protected override void OnFontChanged(EventArgs args)
        {
            base.OnFontChanged(args);
            ItemHeight = Math.Max(20, Font.Height + 4);
        }

        internal void UpdateTheme()
        {
            BackColor = Appearance.BackgroundColor; ForeColor = Appearance.ForegroundColor;
            if (IsHandleCreated)
            {
                try
                {
                    bool dark = Appearance.CurrentTheme == AppTheme.Dark;
                    SetWindowTheme(Handle, dark ? "DarkMode_CFD" : null, null);
                    var info = new ComboBoxInfo { Size = Marshal.SizeOf(typeof(ComboBoxInfo)) };
                    if (GetComboBoxInfo(Handle, ref info) && info.List != IntPtr.Zero)
                        SetWindowTheme(info.List, dark ? "DarkMode_Explorer" : null, null);
                }
                catch { }
            }
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs args)
        {
            bool selected = (args.State & DrawItemState.Selected) != 0 && (args.State & DrawItemState.ComboBoxEdit) == 0;
            Color background = selected ? Appearance.CardTint(null) : Appearance.BackgroundColor;
            using (var brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
            string text = args.Index >= 0 && args.Index < Items.Count ? GetItemText(Items[args.Index]) : GetItemText(SelectedItem);
            var bounds = new Rectangle(args.Bounds.X + 6, args.Bounds.Y, Math.Max(1, args.Bounds.Width - 12), args.Bounds.Height);
            TextRenderer.DrawText(args.Graphics, text, Font, bounds, Enabled ? Appearance.ForegroundColor : Appearance.SecondaryTextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if ((args.State & DrawItemState.Focus) != 0 && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(args.Graphics, Rectangle.Inflate(args.Bounds, -2, -2), Appearance.ForegroundColor, background);
        }

        protected override void OnSelectedIndexChanged(EventArgs args) { base.OnSelectedIndexChanged(args); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs args) { base.OnEnabledChanged(args); Invalidate(); }
        protected override void OnGotFocus(EventArgs args) { base.OnGotFocus(args); Invalidate(); }
        protected override void OnLostFocus(EventArgs args) { base.OnLostFocus(args); Invalidate(); }
        protected override void OnDropDown(EventArgs args) { base.OnDropDown(args); Invalidate(); }
        protected override void OnDropDownClosed(EventArgs args) { base.OnDropDownClosed(args); Invalidate(); }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x000F && IsHandleCreated)
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle)) PaintSurface(graphics);
            }
            else if ((message.Msg == 0x0317 || message.Msg == 0x0318) && message.WParam != IntPtr.Zero)
            {
                using (Graphics graphics = Graphics.FromHdc(message.WParam)) PaintSurface(graphics);
            }
        }

        private void PaintSurface(Graphics graphics)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width < 2 || bounds.Height < 2) return;
            using (var brush = new SolidBrush(Appearance.BackgroundColor)) graphics.FillRectangle(brush, bounds);
            int arrowWidth = Math.Min(bounds.Width, Math.Max(24, bounds.Height));
            var arrow = new Rectangle(bounds.Right - arrowWidth, 1, arrowWidth - 1, bounds.Height - 2);
            using (var brush = new SolidBrush(Appearance.CardTint(null))) graphics.FillRectangle(brush, arrow);
            Color foreground = Enabled ? Appearance.ForegroundColor : Appearance.SecondaryTextColor;
            string text = GetItemText(SelectedItem);
            var textBounds = new Rectangle(6, 0, Math.Max(1, bounds.Width - arrowWidth - 12), bounds.Height);
            TextRenderer.DrawText(graphics, text, Font, textBounds, foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            int centerX = arrow.Left + arrow.Width / 2, centerY = arrow.Top + arrow.Height / 2;
            using (var pen = new Pen(foreground, 1.5f))
                graphics.DrawLines(pen, new[] { new Point(centerX - 4, centerY - 2), new Point(centerX, centerY + 2), new Point(centerX + 4, centerY - 2) });
            using (var pen = new Pen(Appearance.BorderColor)) graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics, new Rectangle(3, 3, Math.Max(1, bounds.Width - arrowWidth - 6), Math.Max(1, bounds.Height - 6)), foreground, Appearance.BackgroundColor);
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativeRectangle { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct ComboBoxInfo {
            public int Size; public NativeRectangle ItemBounds, ButtonBounds; public uint ButtonState;
            public IntPtr Combo, Item, List;
        }
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window, string subApp, string subId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetComboBoxInfo(IntPtr window, ref ComboBoxInfo info);
    }

    internal sealed class DiagnosticButton : Button
    {
        private bool hovered, pressed;
        internal DiagnosticButton()
        {
            FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs args) { hovered = true; base.OnMouseEnter(args); Invalidate(); }
        protected override void OnMouseLeave(EventArgs args) { hovered = false; base.OnMouseLeave(args); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs args) { if (args.Button == MouseButtons.Left) pressed = true; base.OnMouseDown(args); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs args) { pressed = false; base.OnMouseUp(args); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs args) { if (args.KeyCode == Keys.Space) pressed = true; base.OnKeyDown(args); Invalidate(); }
        protected override void OnKeyUp(KeyEventArgs args) { if (args.KeyCode == Keys.Space) pressed = false; base.OnKeyUp(args); Invalidate(); }
        protected override void OnGotFocus(EventArgs args) { base.OnGotFocus(args); Invalidate(); }
        protected override void OnLostFocus(EventArgs args) { pressed = false; base.OnLostFocus(args); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs args) { if (!Enabled) pressed = false; base.OnEnabledChanged(args); Invalidate(); }
        protected override void OnPaint(PaintEventArgs args)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width < 2 || bounds.Height < 2) return;
            Color background = Enabled && (hovered || pressed) ? Appearance.CardTint(null) : Appearance.BackgroundColor;
            Color foreground = Enabled ? Appearance.ForegroundColor : Appearance.SecondaryTextColor;
            using (var brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, bounds);
            using (var pen = new Pen(Focused && Enabled ? Color.FromArgb(139, 109, 239) : Appearance.BorderColor))
                args.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
            var textBounds = new Rectangle(Padding.Left + 3, Padding.Top + 1,
                Math.Max(1, bounds.Width - Padding.Horizontal - 6), Math.Max(1, bounds.Height - Padding.Vertical - 2));
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(args.Graphics, Text, Font, textBounds, foreground, flags);
            if (Focused && Enabled && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(args.Graphics, Rectangle.Inflate(bounds, -4, -4), foreground, background);
        }
    }

    internal sealed class DiagnosticProgress : Control
    {
        private int minimum, maximum = 30, value;
        public int Minimum { get { return minimum; } set { minimum = value; if (maximum < minimum) maximum = minimum; Value = this.value; Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = value; if (minimum > maximum) minimum = maximum; Value = this.value; Invalidate(); } }
        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(minimum, Math.Min(maximum, value));
                bool changed = this.value != next;
                this.value = next;
                Text = next.ToString(System.Globalization.CultureInfo.InvariantCulture) + " / " + maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (changed && IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                Invalidate();
            }
        }
        internal DiagnosticProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AccessibleRole = AccessibleRole.ProgressBar; TabStop = false; Value = 0;
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ProgressAccessibleObject(this); }
        private sealed class ProgressAccessibleObject : ControlAccessibleObject
        {
            private readonly DiagnosticProgress progress;
            internal ProgressAccessibleObject(DiagnosticProgress progress) : base(progress) { this.progress = progress; }
            public override string Value { get { return progress.Text; } set { } }
            public override AccessibleStates State { get { return base.State | AccessibleStates.ReadOnly; } }
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            Rectangle bounds = ClientRectangle;
            if (bounds.Width < 2 || bounds.Height < 2) return;
            using (var brush = new SolidBrush(Appearance.CardTint(null))) args.Graphics.FillRectangle(brush, bounds);
            Rectangle inner = Rectangle.Inflate(bounds, -2, -2);
            long range = (long)maximum - minimum;
            int width = range <= 0 ? 0 : (int)(inner.Width * ((long)value - minimum) / range);
            if (width > 0 && inner.Height > 0)
                using (var brush = new SolidBrush(Color.FromArgb(139, 109, 239))) args.Graphics.FillRectangle(brush, inner.X, inner.Y, width, inner.Height);
            using (var pen = new Pen(Appearance.BorderColor)) args.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
        }
    }
}
