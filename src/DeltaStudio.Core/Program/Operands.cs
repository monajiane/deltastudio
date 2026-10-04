using System.Globalization;
using DeltaStudio.Core.Devices;

namespace DeltaStudio.Core.Program;

/// <summary>Base class of instruction operands.</summary>
public abstract class Operand
{
    /// <summary>Renders the operand in Delta IL/assembly textual form.</summary>
    public abstract string ToText();

    /// <summary>
    /// Parses a textual operand: a device address ("X0", "D100") or a constant
    /// ("K100", "K-5", "H0A1F").
    /// </summary>
    public static bool TryParse(string text, out Operand? operand, out string? error)
    {
        operand = null;
        error = null;
        text = text.Trim();

        if (text.Length > 1 && (text[0] is 'K' or 'k'))
        {
            string body = text[1..];
            bool negative = body.StartsWith('-');
            if (negative)
            {
                body = body[1..];
            }

            if (long.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out long v))
            {
                operand = new ConstantOperand(negative ? -v : v, isHex: false);
                return true;
            }

            error = $"'{text}' is not a valid K constant.";
            return false;
        }

        if (text.Length > 1 && (text[0] is 'H' or 'h'))
        {
            string body = text[1..];
            if (long.TryParse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v))
            {
                operand = new ConstantOperand(v, isHex: true);
                return true;
            }

            error = $"'{text}' is not a valid H constant.";
            return false;
        }

        if (DeviceAddress.TryParse(text, out DeviceAddress addr, out error))
        {
            operand = new DeviceOperand(addr);
            return true;
        }

        error = error ?? $"'{text}' is neither a device address nor a K/H constant.";
        return false;
    }

    /// <summary>Parses or throws <see cref="FormatException"/>.</summary>
    public static Operand Parse(string text)
    {
        if (!TryParse(text, out Operand? o, out string? error))
        {
            throw new FormatException(error);
        }

        return o!;
    }
}

/// <summary>Device reference operand.</summary>
public sealed class DeviceOperand(DeviceAddress address) : Operand
{
    /// <summary>Referenced device.</summary>
    public DeviceAddress Address { get; } = address;

    /// <inheritdoc/>
    public override string ToText() => Address.ToString();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DeviceOperand d && d.Address == Address;

    /// <inheritdoc/>
    public override int GetHashCode() => Address.GetHashCode();
}

/// <summary>K/H constant operand (value stored numerically; hex-ness remembered for round-trip fidelity).</summary>
public sealed class ConstantOperand(long value, bool isHex = false) : Operand
{
    /// <summary>Constant value.</summary>
    public long Value { get; } = value;

    /// <summary>True when written as H (hex) rather than K (decimal).</summary>
    public bool IsHex { get; } = isHex;

    /// <inheritdoc/>
    public override string ToText() => IsHex
        ? "H" + Value.ToString("X", CultureInfo.InvariantCulture)
        : "K" + Value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ConstantOperand c && c.Value == Value && c.IsHex == IsHex;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Value, IsHex);
}

/// <summary>An instruction invocation: mnemonic plus operands.</summary>
public sealed class InstructionCall
{
    /// <summary>Mnemonic as typed; canonical comparison is case-insensitive.</summary>
    public required string Mnemonic { get; init; }

    /// <summary>Operands in catalog order.</summary>
    public List<Operand> Operands { get; } = new();

    /// <summary>Canonical text form, e.g. "MOV D0 K100".</summary>
    public string ToText() =>
        Operands.Count == 0 ? Mnemonic : Mnemonic + " " + string.Join(" ", Operands.Select(o => o.ToText()));
}
