using System;
using AirPodsMicBridge;

// Bounds/architecture checks only. Does not construct a window or receive real input.
internal static class RawInputChecks
{
    private static int checks;

    public static int Main(string[] args)
    {
        try
        {
            ValidReport(4); ValidReport(8);
            InvalidReports();
            Console.WriteLine("Raw input checks passed: " + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }

    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static byte[] Packet(int pointerSize, int size, int count, int padding)
    {
        int header = 8 + pointerSize * 2;
        byte[] packet = new byte[header + 8 + size * count + padding];
        Put(packet, 0, 2); Put(packet, 4, (uint)(packet.Length - padding));
        Put(packet, header, (uint)size); Put(packet, header + 4, (uint)count);
        for (int i = 0; i < size * count; i++) packet[header + 8 + i] = (byte)(i % 251 + 1);
        for (int i = packet.Length - padding; i < packet.Length; i++) packet[i] = 0xEF;
        return packet;
    }

    private static void Put(byte[] bytes, int offset, uint value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
    }

    private static void ValidReport(int pointerSize)
    {
        byte[] output; int size, count; string error;
        byte[] packet = Packet(pointerSize, 3, 2, 0);
        Assert(DiagnosticForm.TryParseRawHid(packet, pointerSize, out output, out size, out count, out error), "Valid report rejected for pointer size " + pointerSize);
        Assert(size == 3 && count == 2 && error == null, "Report metadata changed.");
        Assert(output.Length == 6 && output[0] == 1 && output[5] == 6, "HID reports were read at the wrong architecture offset.");

        packet = Packet(pointerSize, 2, 1, 9);
        Assert(DiagnosticForm.TryParseRawHid(packet, pointerSize, out output, out size, out count, out error), "Valid packet with unused tail was rejected.");
        Assert(output.Length == 2 && output[1] == 2, "Bytes beyond the declared HID payload leaked into the report.");

        packet = Packet(pointerSize, 4096, 1, 0);
        Assert(DiagnosticForm.TryParseRawHid(packet, pointerSize, out output, out size, out count, out error), "Exact payload limit was rejected.");
        Assert(output.Length == 4096 && error == null, "Exact payload limit was incorrectly truncated.");

        packet = Packet(pointerSize, 2000, 3, 0);
        Assert(DiagnosticForm.TryParseRawHid(packet, pointerSize, out output, out size, out count, out error), "Oversized valid payload should retain capped diagnostic bytes.");
        Assert(output.Length == 4096 && size == 2000 && count == 3 && error == "ReportDataTruncated", "Payload cap or original report metadata was lost.");
        Assert(output[4095] == (byte)(4095 % 251 + 1), "Capped payload has the wrong offset.");
    }

    private static void Reject(byte[] packet, int pointerSize, string label)
    {
        byte[] output; int size, count; string error;
        Assert(!DiagnosticForm.TryParseRawHid(packet, pointerSize, out output, out size, out count, out error), label + " accepted.");
        Assert(output == null && size == 0 && count == 0 && !String.IsNullOrEmpty(error), label + " left payload data available.");
    }

    private static void InvalidReports()
    {
        Reject(null, 8, "Null packet");
        Reject(new byte[0], 8, "Empty packet");
        Reject(Packet(8, 2, 1, 0), 16, "Unsupported pointer size");
        foreach (int pointerSize in new[] { 4, 8 })
        {
            int header = 8 + 2 * pointerSize;
            Reject(new byte[header + 7], pointerSize, "Truncated RAWHID header");
            byte[] packet = Packet(pointerSize, 2, 1, 0); Put(packet, 0, 1);
            Reject(packet, pointerSize, "Ordinary keyboard packet");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, 0, 0);
            Reject(packet, pointerSize, "Mouse packet");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, 4, (uint)(packet.Length + 1));
            Reject(packet, pointerSize, "Declared packet exceeds buffer");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, 4, (uint)(header + 7));
            Reject(packet, pointerSize, "Declared packet lacks HID header");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, header, 0);
            Reject(packet, pointerSize, "Zero report size");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, header + 4, 0);
            Reject(packet, pointerSize, "Zero report count");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, header + 4, 2);
            Reject(packet, pointerSize, "Report count exceeds payload");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, header, UInt32.MaxValue);
            Reject(packet, pointerSize, "Unsigned report size exceeds managed limits");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, header + 4, UInt32.MaxValue);
            Reject(packet, pointerSize, "Unsigned report count exceeds managed limits");
            packet = Packet(pointerSize, 2, 1, 0); Put(packet, header, Int32.MaxValue); Put(packet, header + 4, Int32.MaxValue);
            Reject(packet, pointerSize, "Report multiplication would overflow 32 bits");
        }
    }
}
