using System.Globalization;

namespace DeltaStudio.Core.Devices;

/// <summary>Device class of a DVP-style bit/word device.</summary>
public enum DeviceKind
{
    /// <summary>External digital inputs (octal numbering).</summary>
    X,
    /// <summary>External digital outputs (octal numbering).</summary>
    Y,
    /// <summary>Auxiliary (internal) relays.</summary>
    M,
    /// <summary>State/step relays (used by SFC/STL).</summary>
    S,
    /// <summary>Latched relays.</summary>
    L,
    /// <summary>Timers (contact + current value).</summary>
    T,
    /// <summary>Counters (contact + current value).</summary>
    C,
    /// <summary>Data registers (16-bit words).</summary>
    D,
}

/// <summary>How a device may be accessed by the runtime.</summary>
public enum DeviceAccess
{
    /// <summary>Reflects physical inputs; the program must not write it.</summary>
    ReadOnly,
    /// <summary>Read and write.</summary>
    ReadWrite,
    /// <summary>Contact is read-only, present value is read/write (T, C).</summary>
    ContactAndValue,
}

/// <summary>
/// A parsed device address such as X0, Y17 (octal) or M100, D500 (decimal).
/// <see cref="Number"/> is the zero-based linear index; for X/Y the decimal number of the
/// octal digit string (X17 ⇒ 15). The struct is display-format aware but range-agnostic:
/// validity is decided by <see cref="Model.PlcModelDefinition"/> capabilities.
/// </summary>
public readonly struct DeviceAddress : IEquatable<DeviceAddress>
{
    /// <summary>Device class.</summary>
    public DeviceKind Kind { get; }

    /// <summary>Zero-based linear index (decimal value of the printed number; octal for X/Y).</summary>
    public int Number { get; }

    /// <summary>Creates an address from its linear index.</summary>
    public DeviceAddress(DeviceKind kind, int number)
    {
        if (number < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Device index must be >= 0.");
        }

        Kind = kind;
        Number = number;
    }

    /// <summary>True when this device class is written with octal digits (X and Y on DVP).</summary>
    public static bool IsOctalKind(DeviceKind kind) => kind is DeviceKind.X or DeviceKind.Y;

    /// <summary>Parses a textual address like "X17" or "D100". Returns false with a reason when invalid.</summary>
    public static bool TryParse(string text, out DeviceAddress address, out string? error)
    {
        address = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Address is empty.";
            return false;
        }

        text = text.Trim().ToUpperInvariant();
        if (text.Length < 2)
        {
            error = $"'{text}' is not a device address.";
            return false;
        }

        char prefix = text[0];
        DeviceKind kind = prefix switch
        {
            'X' => DeviceKind.X,
            'Y' => DeviceKind.Y,
            'M' => DeviceKind.M,
            'S' => DeviceKind.S,
            'L' => DeviceKind.L,
            'T' => DeviceKind.T,
            'C' => DeviceKind.C,
            'D' => DeviceKind.D,
            _ => (DeviceKind)(-1),
        };

        if ((int)kind == -1)
        {
            error = $"'{prefix}' is not a device prefix (expected X, Y, M, S, L, T, C or D).";
            return false;
        }

        string digits = text[1..];
        bool octal = IsOctalKind(kind);
        int radix = octal ? 8 : 10;

        foreach (char c in digits)
        {
            if (octal && c is >= '8' and <= '9')
            {
                error = $"'{text}': X/Y addresses are octal, digit '{c}' is not allowed.";
                return false;
            }

            if (c is not (>= '0' and <= '9'))
            {
                error = $"'{text}': invalid character '{c}'.";
                return false;
            }
        }

        if (!TryParseRadix(digits, radix, out int value) || value < 0)
        {
            // guard against overflow of huge digit strings
            error = $"'{text}': invalid device number.";
            return false;
        }

        address = new DeviceAddress(kind, value);
        return true;
    }

    /// <summary>Parses a textual address; throws <see cref="FormatException"/> when invalid.</summary>
    public static DeviceAddress Parse(string text)
    {
        if (!TryParse(text, out DeviceAddress a, out string? error))
        {
            throw new FormatException(error);
        }

        return a;
    }

    /// <summary>Textual form exactly as it appears in Delta manuals (X17 for index 15, D100 for index 100).</summary>
    public override string ToString()
    {
        string number = IsOctalKind(Kind)
            ? Convert.ToString(Number, 8)
            : Number.ToString(CultureInfo.InvariantCulture);
        return $"{(char)KindPrefix(Kind)}{number}";
    }

    private static bool TryParseRadix(string digits, int radix, out int value)
    {
        if (radix == 10)
        {
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        value = 0;
        foreach (char c in digits)
        {
            value = value * radix + (c - '0');
            if (value > 0x7FFFFFFF / 8)
            {
                return false;
            }
        }

        return true;
    }

    private static int KindPrefix(DeviceKind kind) => kind switch
    {
        DeviceKind.X => 'X',
        DeviceKind.Y => 'Y',
        DeviceKind.M => 'M',
        DeviceKind.S => 'S',
        DeviceKind.L => 'L',
        DeviceKind.T => 'T',
        DeviceKind.C => 'C',
        DeviceKind.D => 'D',
        _ => '?',
    };

    /// <inheritdoc/>
    public bool Equals(DeviceAddress other) => Kind == other.Kind && Number == other.Number;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DeviceAddress a && Equals(a);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Number);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(DeviceAddress left, DeviceAddress right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(DeviceAddress left, DeviceAddress right) => !left.Equals(right);
}
