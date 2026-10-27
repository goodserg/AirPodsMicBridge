using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AirPodsMicBridge;

// Generated PCM fixtures and an injected playback backend: no real audio is played.
internal static class MicrophoneSoundChecks
{
    private static int checks;

    public static int Main()
    {
        try
        {
            byte[] muted = MicrophoneSounds.CreateWave(true);
            byte[] live = MicrophoneSounds.CreateWave(false);
            CheckWave(muted, true);
            CheckWave(live, false);
            Assert(!Equal(muted, live), "Mute and live cues must be distinct");
            Assert(Equal(muted, MicrophoneSounds.CreateWave(true)), "Mute cue must be deterministic");
            Assert(Equal(live, MicrophoneSounds.CreateWave(false)), "Live cue must be deterministic");
            CheckPlayback(muted, live);
            CheckFailure();
            Console.WriteLine("Microphone sound checks passed: " + checks);
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

    private static void CheckWave(byte[] wave, bool muted)
    {
        using (var stream = new MemoryStream(wave, false))
        using (var reader = new BinaryReader(stream, Encoding.ASCII))
        {
            Assert(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "RIFF", "RIFF header");
            Assert(reader.ReadInt32() == wave.Length - 8, "RIFF length");
            Assert(Encoding.ASCII.GetString(reader.ReadBytes(8)) == "WAVEfmt ", "WAVE and format chunks");
            Assert(reader.ReadInt32() == 16, "PCM format length");
            Assert(reader.ReadInt16() == 1, "PCM format");
            Assert(reader.ReadInt16() == 1, "Mono output");
            Assert(reader.ReadInt32() == 24000, "Sample rate");
            Assert(reader.ReadInt32() == 48000, "Byte rate");
            Assert(reader.ReadInt16() == 2, "Sample block alignment");
            Assert(reader.ReadInt16() == 16, "16 bit sample depth");
            Assert(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "data", "Data chunk");
            Assert(reader.ReadInt32() == wave.Length - 44, "Data length");
        }
        Assert(wave.Length == 12524, "Cue duration remains 260 ms");
        int peak = 0;
        double sumSquares = 0;
        for (int index = 44; index < wave.Length; index += 2)
        {
            int sample = Math.Abs((int)BitConverter.ToInt16(wave, index));
            peak = Math.Max(peak, sample);
            sumSquares += sample * (double)sample;
        }
        Assert(peak > 3000 && peak < 6554, "Useful signal with at least 14 dB peak headroom");
        Assert(Math.Sqrt(sumSquares / 6240) > 1200, "Cue must be audible, not empty or a click");
        Assert(Silent(wave, 0, 192), "Leading 8 ms must be silent");
        Assert(Silent(wave, 5760, 6240), "Trailing 20 ms must be silent");
        Assert(Silent(wave, 2880, 3072), "Notes must be separated by a short silence");
        Assert(MaxAdjacentStep(wave) < 1800, "Smooth waveform without full-scale discontinuities");
        double first = EstimateFrequency(wave, 0.025, 0.085);
        double second = EstimateFrequency(wave, 0.145, 0.205);
        Assert(Math.Abs(first - (muted ? 880 : 660)) < 20, "First note pitch");
        Assert(Math.Abs(second - (muted ? 660 : 880)) < 20, "Second note pitch");
        Assert(muted ? first > second + 150 : second > first + 150, "Correct audible direction for state");
    }

    private static bool Silent(byte[] wave, int startSample, int endSample)
    {
        for (int index = startSample; index < endSample; index++)
            if (BitConverter.ToInt16(wave, 44 + index * 2) != 0) return false;
        return true;
    }

    private static int MaxAdjacentStep(byte[] wave)
    {
        int maximum = 0;
        for (int index = 46; index < wave.Length; index += 2)
            maximum = Math.Max(maximum, Math.Abs(BitConverter.ToInt16(wave, index) - BitConverter.ToInt16(wave, index - 2)));
        return maximum;
    }

    private static double EstimateFrequency(byte[] wave, double start, double end)
    {
        int first = (int)(start * 24000), last = (int)(end * 24000), crossings = 0;
        for (int sample = first + 1; sample < last; sample++)
            if (BitConverter.ToInt16(wave, 44 + (sample - 1) * 2) <= 0 && BitConverter.ToInt16(wave, 44 + sample * 2) > 0) crossings++;
        return crossings / (end - start);
    }

    private static void CheckPlayback(byte[] muted, byte[] live)
    {
        int calls = 0, stops = 0;
        byte[] lastWave = null;
        uint lastFlags = 0;
        var output = new MicrophoneSounds(delegate(IntPtr address, uint flags)
        {
            calls++;
            lastFlags = flags;
            if (address == IntPtr.Zero) { stops++; return true; }
            lastWave = new byte[12524];
            Marshal.Copy(address, lastWave, 0, lastWave.Length);
            return true;
        });
        Assert(calls == 0, "Construction must not play a startup cue");
        Assert(output.Play(true), "Muted cue accepted");
        Assert(Equal(lastWave, muted), "Muted state selects descending PCM");
        Assert(lastFlags == 7, "Playback is asynchronous, from memory, with no fallback beep");
        Assert(output.Play(false), "Live cue accepted");
        Assert(Equal(lastWave, live), "Live state selects ascending PCM");
        Assert(calls == 2, "Rapid cues replace playback without a queue or retry");
        output.Dispose();
        Assert(stops == 1 && calls == 3 && lastFlags == 0, "Dispose stops native playback before memory release");
        output.Dispose();
        Assert(stops == 1 && calls == 3, "Dispose is idempotent");
        Assert(!output.Play(true) && calls == 3, "Disposed output never touches released native memory");
        using (var unused = new MicrophoneSounds(delegate(IntPtr address, uint flags) { throw new InvalidOperationException("Should not run"); })) { }
        Assert(true, "Unused instance disposal does not interrupt unrelated playback");
    }

    private static void CheckFailure()
    {
        int calls = 0;
        using (var missingDevice = new MicrophoneSounds(delegate(IntPtr address, uint flags) { calls++; return false; }))
        {
            Assert(!missingDevice.Play(true), "Missing playback device is nonfatal");
            Assert(!missingDevice.Play(false), "A failed cue does not block subsequent attempts");
        }
        Assert(calls == 2, "Failed playback does not queue/retry or stop unrelated sounds");
        using (var missingLibrary = new MicrophoneSounds(delegate(IntPtr address, uint flags) { throw new DllNotFoundException(); }))
            Assert(!missingLibrary.Play(true), "Missing native audio library is nonfatal");
        using (var unavailableFunction = new MicrophoneSounds(delegate(IntPtr address, uint flags) { throw new EntryPointNotFoundException(); }))
            Assert(!unavailableFunction.Play(false), "Unavailable audio entry point is nonfatal");
        using (var nativeError = new MicrophoneSounds(delegate(IntPtr address, uint flags) { throw new ExternalException(); }))
            Assert(!nativeError.Play(false), "Native playback failure is nonfatal");
        using (var denied = new MicrophoneSounds(delegate(IntPtr address, uint flags) { throw new System.Security.SecurityException(); }))
            Assert(!denied.Play(true), "Playback permission failure is nonfatal");
    }

    private static bool Equal(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
        return true;
    }
}
