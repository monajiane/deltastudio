namespace DeltaStudio.Protocols.Modbus;

/// <summary>Modbus RTU CRC16 (poly 0xA001, init 0xFFFF) — per MODBUS over Serial Line spec V1.02.</summary>
public static class ModbusCrc
{
    /// <summary>Computes the CRC over the buffer.</summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return crc;
    }

    /// <summary>Appends little-endian CRC and returns the full RTU frame.</summary>
    public static byte[] Append(ReadOnlySpan<byte> payload)
    {
        byte[] frame = new byte[payload.Length + 2];
        payload.CopyTo(frame);
        ushort crc = Compute(payload);
        frame[^2] = (byte)(crc & 0xFF);
        frame[^1] = (byte)(crc >> 8);
        return frame;
    }

    /// <summary>Validates the trailing CRC of a complete RTU frame.</summary>
    public static bool Validate(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 4)
        {
            return false;
        }

        ushort expected = Compute(frame[..^2]);
        ushort actual = (ushort)(frame[^2] | (frame[^1] << 8));
        return expected == actual;
    }
}

/// <summary>Modbus ASCII codec: ':' + hex + LRC + CRLF, per MODBUS over Serial Line spec V1.02.</summary>
public static class ModbusAsciiCodec
{
    /// <summary>Encodes a PDU (station byte included) into an ASCII frame.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> adu)
    {
        byte lrc = 0;
        foreach (byte b in adu)
        {
            lrc = (byte)(lrc + b);
        }

        lrc = (byte)(0 - lrc);

        var chars = new List<char>(2 + adu.Length * 2 + 4);
        chars.Add(':');
        foreach (byte b in adu)
        {
            chars.Add(HexDigit(b >> 4));
            chars.Add(HexDigit(b & 0xF));
        }

        chars.Add(HexDigit(lrc >> 4));
        chars.Add(HexDigit(lrc & 0xF));
        chars.Add('\r');
        chars.Add('\n');
        return System.Text.Encoding.ASCII.GetBytes(chars.ToArray());
    }

    /// <summary>Decodes an ASCII frame; returns null when malformed or LRC fails.</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> frame)
    {
        string text = System.Text.Encoding.ASCII.GetString(frame).Trim();
        if (text.Length < 5 || text[0] != ':')
        {
            return null;
        }

        text = text[1..];
        if (text.Length % 2 != 0)
        {
            return null;
        }

        byte[] bytes = new byte[text.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            int hi = FromHex(text[i * 2]);
            int lo = FromHex(text[i * 2 + 1]);
            if (hi < 0 || lo < 0)
            {
                return null;
            }

            bytes[i] = (byte)((hi << 4) | lo);
        }

        if (bytes.Length < 2)
        {
            return null;
        }

        byte sum = 0;
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            sum += bytes[i];
        }

        if ((byte)(sum + bytes[^1]) != 0)
        {
            return null; // bad LRC
        }

        return bytes[..^1];
    }

    private static char HexDigit(int v) => (char)(v < 10 ? '0' + v : 'A' + v - 10);

    private static int FromHex(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };
}
