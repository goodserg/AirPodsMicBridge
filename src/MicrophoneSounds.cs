using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace AirPodsMicBridge
{
    // Playback only: these original, generated cues never create a capture session.
    // PlaySound is asynchronous and replaces the previous cue instead of queuing it.
    internal sealed class MicrophoneSounds : IDisposable
    {
        internal const uint AsyncMemoryFlags = 0x0001 | 0x0002 | 0x0004; // ASYNC | NODEFAULT | MEMORY
        internal delegate bool WavePlayback(IntPtr wave, uint flags);
        private readonly object gate = new object();
        private readonly WavePlayback playback;
        private GCHandle mutedWave;
        private GCHandle liveWave;
        private bool playing, disposed;

        internal MicrophoneSounds() : this(PlayNative) { }

        // The injectable backend lets checks exercise lifecycle and failure handling silently.
        internal MicrophoneSounds(WavePlayback playback)
        {
            if (playback == null) throw new ArgumentNullException("playback");
            this.playback = playback;
            try
            {
                mutedWave = GCHandle.Alloc(CreateWave(true), GCHandleType.Pinned);
                liveWave = GCHandle.Alloc(CreateWave(false), GCHandleType.Pinned);
            }
            catch
            {
                if (mutedWave.IsAllocated) mutedWave.Free();
                if (liveWave.IsAllocated) liveWave.Free();
                throw;
            }
        }

        // A missing/disabled output device must never affect microphone control.
        // NODEFAULT also prevents Windows substituting an unrelated error beep.
        internal bool Play(bool muted)
        {
            lock (gate)
            {
                if (disposed) return false;
                bool accepted = TryPlay((muted ? mutedWave : liveWave).AddrOfPinnedObject(), AsyncMemoryFlags);
                if (accepted) playing = true;
                return accepted;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                // Stop the native read before releasing its pinned memory.
                if (playing) TryPlay(IntPtr.Zero, 0);
                if (mutedWave.IsAllocated) mutedWave.Free();
                if (liveWave.IsAllocated) liveWave.Free();
            }
        }

        private bool TryPlay(IntPtr wave, uint flags)
        {
            try { return playback(wave, flags); }
            catch (ExternalException) { return false; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (SecurityException) { return false; }
        }

        private static bool PlayNative(IntPtr wave, uint flags)
        {
            return PlaySound(wave, IntPtr.Zero, flags);
        }

        [DllImport("winmm.dll", EntryPoint = "PlaySoundW", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PlaySound(IntPtr wave, IntPtr module, uint flags);

        // 260 ms, 24 kHz, mono PCM. A soft descending pair means muted, ascending means live.
        // Every note has smooth attack/release ramps and generous headroom for headphones.
        internal static byte[] CreateWave(bool muted)
        {
            const int sampleRate = 24000, sampleCount = 6240;
            const int dataBytes = sampleCount * 2;
            double first = muted ? 880 : 660;
            double second = muted ? 660 : 880;
            using (var stream = new MemoryStream(44 + dataBytes))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // Mono
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                for (int index = 0; index < sampleCount; index++)
                {
                    double time = index / (double)sampleRate;
                    double value = Note(time - 0.008, first) + Note(time - 0.128, second);
                    writer.Write((short)Math.Round(value * Int16.MaxValue));
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static double Note(double time, double frequency)
        {
            const double duration = 0.112, attack = 0.009, release = 0.020;
            if (time <= 0 || time >= duration) return 0;
            double envelope = Math.Exp(-5 * time);
            if (time < attack) envelope *= Math.Pow(Math.Sin(Math.PI * time / (2 * attack)), 2);
            if (time > duration - release) envelope *= Math.Pow(Math.Sin(Math.PI * (duration - time) / (2 * release)), 2);
            double phase = 2 * Math.PI * frequency * time;
            return 0.18 * envelope * (0.86 * Math.Sin(phase) + 0.10 * Math.Sin(2 * phase) + 0.04 * Math.Sin(3 * phase));
        }
    }
}
