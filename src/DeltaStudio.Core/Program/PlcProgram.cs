namespace DeltaStudio.Core.Program;

/// <summary>
/// Vendor-neutral program container. DVP main ladder programs are a flat rung list; the
/// container exists so sub-programs/interrupt routines can be added by future vendors or
/// by the Delta backend once verified (currently: not represented — see docs/delta-dvp.md).
/// </summary>
public sealed class PlcProgram
{
    /// <summary>Main ladder program rungs, in order.</summary>
    public List<Rung> Main { get; } = new();

    /// <summary>Adds a rung and returns it.</summary>
    public Rung AddRung(string? comment = null)
    {
        var rung = new Rung { Comment = comment };
        Main.Add(rung);
        return rung;
    }

    /// <summary>Removes a rung by index; returns false when out of range.</summary>
    public bool RemoveRung(int index)
    {
        if (index < 0 || index >= Main.Count)
        {
            return false;
        }

        Main.RemoveAt(index);
        return true;
    }

    /// <summary>Moves a rung; used by the editor and by MCP rung reorder operations.</summary>
    public void MoveRung(int from, int to)
    {
        if (from < 0 || to < 0 || from >= Main.Count || to >= Main.Count)
        {
            throw new ArgumentOutOfRangeException($"Cannot move rung {from} to {to} (count {Main.Count}).");
        }

        Rung r = Main[from];
        Main.RemoveAt(from);
        Main.Insert(to, r);
    }
}

/// <summary>Named symbolic alias of one device address.</summary>
public sealed class SymbolDefinition
{
    /// <summary>Symbolic name (unique per program, case-insensitive).</summary>
    public required string Name { get; init; }

    /// <summary>Alias for an absolute device.</summary>
    public required Devices.DeviceAddress Address { get; init; }

    /// <summary>Free-form comment.</summary>
    public string? Comment { get; init; }
}
