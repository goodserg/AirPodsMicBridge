using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MagicPodsR7Bridge
{
    public sealed class CaptureEndpointSnapshot
    {
        public string EndpointId { get; set; }
        public string FriendlyName { get; set; }
        public bool IsAirPods { get; set; }
        public bool DefaultCommunications { get; set; }
        public bool? Muted { get; set; }
        public bool HasActiveR7Capture { get; set; }
        public int[] ActiveR7ProcessIds { get; set; }
        public string Error { get; set; }
    }
    public sealed class AudioStateSnapshot
    {
        public CaptureEndpointSnapshot Target { get; set; }
        public CaptureEndpointSnapshot[] CaptureEndpoints { get; set; }
        public string Error { get; set; }
        public string EndpointId { get { return Target == null ? null : Target.EndpointId; } }
        public string EndpointName { get { return Target == null ? null : Target.FriendlyName; } }
        public bool EndpointActive { get { return Target != null; } }
        public bool? EndpointMuted { get { return Target == null ? null : Target.Muted; } }
        public bool HasActiveR7Capture { get { return Target != null && Target.HasActiveR7Capture; } }
        // Eligibility reflects active R7 microphone use, not proof of a connected call; the caller gates handling.
        public bool CanArm { get { return Target != null && Target.IsAirPods && Target.HasActiveR7Capture && String.IsNullOrEmpty(Error) && String.IsNullOrEmpty(Target.Error); } }
    }

    // Reads Windows' existing capture peak meter; never opens or records an audio stream.
    // Construct, read and dispose on the same MTA worker thread. Peak is before endpoint mute.
    public sealed class CapturePeakMeter : IDisposable
    {
        private const uint Active = 1, ClsctxAll = 23;
        private static readonly Guid MeterIid = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
        private readonly int ownerThreadId;
        private CoreAudioAdapter.IMMDeviceEnumerator enumerator;
        private CoreAudioAdapter.IMMDevice device;
        private IAudioMeterInformation meter;
        private bool disposed;

        public string EndpointId { get; private set; }
        public string Error { get; private set; }
        public bool IsAvailable { get { return !disposed && meter != null; } }

        public CapturePeakMeter(string endpointId)
        {
            if (String.IsNullOrWhiteSpace(endpointId))
                throw new ArgumentException("A capture endpoint ID is required.", "endpointId");
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
                throw new InvalidOperationException("CapturePeakMeter requires an MTA worker thread.");
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            EndpointId = endpointId;
            object activated = null;
            try
            {
                enumerator = (CoreAudioAdapter.IMMDeviceEnumerator)new CoreAudioAdapter.MMDeviceEnumeratorComObject();
                Check(enumerator.GetDevice(endpointId, out device));
                uint state; Check(device.GetState(out state));
                if ((state & Active) == 0) throw new InvalidOperationException("CaptureEndpointUnavailable");
                int flow; Check(((IMMEndpoint)device).GetDataFlow(out flow));
                if (flow != 1) throw new InvalidOperationException("NotACaptureEndpoint");
                Guid iid = MeterIid;
                Check(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out activated));
                meter = (IAudioMeterInformation)activated;
                activated = null;
            }
            catch (Exception ex)
            {
                Error = ex.GetType().Name + ":0x" + ex.HResult.ToString("X8");
                ReleaseHandles();
            }
            finally { Release(activated); }
        }

        public float? ReadPeak()
        {
            RequireOwner();
            if (!IsAvailable) return null;
            try
            {
                float peak; Check(meter.GetPeakValue(out peak));
                if (Single.IsNaN(peak) || Single.IsInfinity(peak))
                {
                    Error = "InvalidPeakValue";
                    return null;
                }
                Error = null;
                return Math.Max(0.0f, Math.Min(1.0f, peak));
            }
            catch (Exception ex)
            {
                Error = ex.GetType().Name + ":0x" + ex.HResult.ToString("X8");
                ReleaseHandles();
                return null;
            }
        }

        public void Dispose()
        {
            RequireOwner();
            if (disposed) return;
            disposed = true;
            ReleaseHandles();
        }

        private void RequireOwner()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId ||
                Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
                throw new InvalidOperationException("Use CapturePeakMeter only on its owning MTA worker thread.");
        }
        private void ReleaseHandles()
        {
            object oldMeter = meter, oldDevice = device, oldEnumerator = enumerator;
            meter = null; device = null; enumerator = null;
            Release(oldMeter); Release(oldDevice); Release(oldEnumerator);
        }
        private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try { Marshal.ReleaseComObject(value); }
                catch (InvalidComObjectException) { }
            }
        }
        [ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMEndpoint {
            [PreserveSig] int GetDataFlow(out int flow);
        }
        [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioMeterInformation {
            [PreserveSig] int GetPeakValue(out float peak);
        }
    }

    public static class CoreAudioAdapter
    {
        private const uint Active = 1, ClsctxAll = 23;
        private const int Capture = 1, Communications = 2;
        private static readonly Guid EndpointVolumeIid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly Guid SessionManager2Iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
        private static readonly object ToggleSync = new object();

        public static AudioStateSnapshot Read(string pinnedEndpointId = null)
        {
            try { return RunMta(() => ReadMta(pinnedEndpointId)); }
            catch (Exception ex) { return new AudioStateSnapshot { CaptureEndpoints = new CaptureEndpointSnapshot[0], Error = ErrorCode(ex) }; }
        }
        // The caller must gate this action and serialize incoming gestures.
        // This method only changes the requested active CAPTURE endpoint, then returns its actual state.
        public static bool Toggle(string endpointId)
        {
            if (String.IsNullOrEmpty(endpointId)) throw new ArgumentException("An endpoint ID is required.", "endpointId");
            lock (ToggleSync) return RunMta(() => ToggleMta(endpointId));
        }
        private static T RunMta<T>(Func<T> action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA) return action();
            T result = default(T); Exception error = null;
            var worker = new Thread(() => { try { result = action(); } catch (Exception ex) { error = ex; } });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
            worker.Join();
            if (error != null) throw error;
            return result;
        }
        private static AudioStateSnapshot ReadMta(string pinned)
        {
            var rows = new List<CaptureEndpointSnapshot>();
            IMMDeviceEnumerator enumerator = null; IMMDeviceCollection devices = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                string defaultId = DefaultId(enumerator);
                Check(enumerator.EnumAudioEndpoints(Capture, Active, out devices));
                uint count; Check(devices.GetCount(out count));
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice device = null;
                    try
                    {
                        Check(devices.Item(index, out device));
                        rows.Add(ReadDevice(device, defaultId));
                    }
                    finally { Release(device); }
                }
                rows.Sort((left, right) => StringComparer.Ordinal.Compare(left.EndpointId, right.EndpointId));
                CaptureEndpointSnapshot target = null;
                int eligibleCount = 0;
                bool anyAirPods = false;
                foreach (var row in rows)
                {
                    anyAirPods |= row.IsAirPods;
                    if (!String.IsNullOrEmpty(pinned))
                    {
                        if (String.Equals(row.EndpointId, pinned, StringComparison.Ordinal)) target = row;
                    }
                    else if (row.IsAirPods && row.HasActiveR7Capture && String.IsNullOrEmpty(row.Error))
                    {
                        eligibleCount++;
                        target = row;
                    }
                }
                string selectionError = null;
                if (String.IsNullOrEmpty(pinned) && eligibleCount != 1)
                {
                    target = null;
                    selectionError = eligibleCount > 1 ? "AmbiguousR7AirPodsCaptureEndpoints" :
                        anyAirPods ? "NoActiveR7AirPodsCapture" : "NoActiveAirPodsCaptureEndpoint";
                }
                else if (target == null) selectionError = "PinnedCaptureEndpointUnavailable";
                return new AudioStateSnapshot { Target = target, CaptureEndpoints = rows.ToArray(), Error = selectionError };
            }
            finally { Release(devices); Release(enumerator); }
        }
        private static CaptureEndpointSnapshot ReadDevice(IMMDevice device, string defaultId)
        {
            string id; Check(device.GetId(out id));
            var row = new CaptureEndpointSnapshot { EndpointId = id, ActiveR7ProcessIds = new int[0], DefaultCommunications = id == defaultId };
            try
            {
                row.FriendlyName = FriendlyName(device);
                row.IsAirPods = row.FriendlyName.IndexOf("AirPods", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex) { row.FriendlyName = id; row.Error = ErrorCode(ex); }
            object volumeObject = null;
            try
            {
                Guid iid = EndpointVolumeIid; Check(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out volumeObject));
                bool muted; Check(((IAudioEndpointVolume)volumeObject).GetMute(out muted)); row.Muted = muted;
            }
            catch (Exception ex) { row.Error = ErrorCode(ex); }
            finally { Release(volumeObject); }
            object managerObject = null; IAudioSessionEnumerator sessions = null;
            try
            {
                Guid iid = SessionManager2Iid; Check(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out managerObject));
                Check(((IAudioSessionManager2)managerObject).GetSessionEnumerator(out sessions));
                int count; Check(sessions.GetCount(out count));
                var r7Ids = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl control = null;
                    try
                    {
                        Check(sessions.GetSession(i, out control));
                        int state; Check(control.GetState(out state)); if (state != 1) continue;
                        uint pid; Check(((IAudioSessionControl2)control).GetProcessId(out pid));
                        try
                        {
                            using (var process = Process.GetProcessById(checked((int)pid)))
                                if (String.Equals(process.ProcessName, "r7-team", StringComparison.OrdinalIgnoreCase)) r7Ids.Add(process.Id);
                        }
                        catch (ArgumentException) { }
                        catch (InvalidOperationException) { }
                    }
                    finally { Release(control); }
                }
                r7Ids.Sort(); row.ActiveR7ProcessIds = r7Ids.ToArray(); row.HasActiveR7Capture = r7Ids.Count > 0;
            }
            catch (Exception ex) { row.Error = ErrorCode(ex); }
            finally { Release(sessions); Release(managerObject); }
            return row;
        }
        private static bool ToggleMta(string endpointId)
        {
            IMMDeviceEnumerator enumerator = null; IMMDeviceCollection devices = null; IMMDevice target = null; object volumeObject = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                Check(enumerator.EnumAudioEndpoints(Capture, Active, out devices));
                uint count; Check(devices.GetCount(out count));
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    try
                    {
                        Check(devices.Item(i, out device)); string id; Check(device.GetId(out id));
                        if (String.Equals(id, endpointId, StringComparison.Ordinal)) { target = device; device = null; break; }
                    }
                    finally { Release(device); }
                }
                if (target == null) throw new InvalidOperationException("The selected capture endpoint is not active.");
                Guid iid = EndpointVolumeIid; Check(target.Activate(ref iid, ClsctxAll, IntPtr.Zero, out volumeObject));
                var volume = (IAudioEndpointVolume)volumeObject; bool muted;
                Check(volume.GetMute(out muted)); Guid context = Guid.Empty;
                bool desired = !muted;
                Check(volume.SetMute(desired, ref context));
                // Bluetooth endpoints can acknowledge SetMute before GetMute reflects the change.
                var confirmation = Stopwatch.StartNew();
                do
                {
                    Check(volume.GetMute(out muted));
                    if (muted == desired) return muted;
                    Thread.Sleep(15);
                } while (confirmation.ElapsedMilliseconds < 1000);
                throw new InvalidOperationException("Windows did not confirm the requested microphone state.");
            }
            finally { Release(volumeObject); Release(target); Release(devices); Release(enumerator); }
        }
        private static string DefaultId(IMMDeviceEnumerator enumerator)
        {
            IMMDevice device = null;
            try { if (enumerator.GetDefaultAudioEndpoint(Capture, Communications, out device) < 0) return null; string id; Check(device.GetId(out id)); return id; }
            finally { Release(device); }
        }
        private static string FriendlyName(IMMDevice device)
        {
            IPropertyStore store = null; PropVariant value = new PropVariant();
            try
            {
                Check(device.OpenPropertyStore(0, out store));
                var key = new PropertyKey { FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
                Check(store.GetValue(ref key, out value));
                return value.VarType == 31 || value.VarType == 8 ? (Marshal.PtrToStringUni(value.Pointer) ?? String.Empty) : String.Empty;
            }
            finally { PropVariantClear(ref value); Release(store); }
        }
        private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        private static string ErrorCode(Exception ex) { return ex.GetType().Name + ":0x" + ex.HResult.ToString("X8"); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid FormatId; public uint PropertyId; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant {
            [FieldOffset(0)] public ushort VarType;
            [FieldOffset(8)] public IntPtr Pointer;
        }
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class MMDeviceEnumeratorComObject { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IMMDeviceEnumerator {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IMMDeviceCollection {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IMMDevice {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
            [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IPropertyStore {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        }
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioEndpointVolume {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
            [PreserveSig] int GetMasterVolumeLevel(out float level);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        }
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionManager2 {
            [PreserveSig] int GetAudioSessionControl(ref Guid sessionId, uint flags, out IAudioSessionControl control);
            [PreserveSig] int GetSimpleAudioVolume(ref Guid sessionId, uint flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionEnumerator {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl control);
        }
        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionControl {
            [PreserveSig] int GetState(out int state);
        }
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioSessionControl2 {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid grouping);
            [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr callback);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr callback);
            [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetProcessId(out uint processId);
        }
    }
}
