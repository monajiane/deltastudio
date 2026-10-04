using DeltaStudio.Core.Devices;

namespace DeltaStudio.Core.Program;

/// <summary>Base class of every element of the ladder intermediate representation.</summary>
public abstract class LadderNode;

/// <summary>Electrical polarity of a contact.</summary>
public enum ContactPolarity
{
    /// <summary>Normally open (LD/AND/OR).</summary>
    NormallyOpen,

    /// <summary>Normally closed (LDI/ANI/ORI).</summary>
    NormallyClosed,

    /// <summary>Rising edge detection (LDP/ANDP/ORP).</summary>
    RisingEdge,

    /// <summary>Falling edge detection (LDF/ANDF/ORF).</summary>
    FallingEdge,
}

/// <summary>Action performed by a coil-type element.</summary>
public enum CoilAction
{
    /// <summary>Periodic refresh output (OUT).</summary>
    Output,

    /// <summary>Set (latch on, keeps state between scans).</summary>
    Set,

    /// <summary>Reset.</summary>
    Reset,
}

/// <summary>A single contact on a rail. Semantics only — layout is metadata, never the model.</summary>
public sealed class ContactNode : LadderNode
{
    /// <summary>Bit device the contact reads.</summary>
    public required DeviceAddress Device { get; init; }

    /// <summary>Polarity.</summary>
    public ContactPolarity Polarity { get; init; } = ContactPolarity.NormallyOpen;

    /// <summary>Optional symbolic label shown in editors.</summary>
    public string? Label { get; init; }
}

/// <summary>A coil writing a bit device.</summary>
public sealed class CoilNode : LadderNode
{
    /// <summary>Bit device the coil writes.</summary>
    public required DeviceAddress Device { get; init; }

    /// <summary>OUT / SET / RST.</summary>
    public CoilAction Action { get; init; } = CoilAction.Output;

    /// <summary>Optional symbolic label shown in editors.</summary>
    public string? Label { get; init; }
}

/// <summary>
/// A generic application instruction rendered as a ladder block (MOV, ADD, CMP, TMR, …).
/// The call itself is the semantic unit; blocks are validated against the instruction catalog.
/// </summary>
public sealed class InstructionNode : LadderNode
{
    /// <summary>The instruction call.</summary>
    public required InstructionCall Call { get; init; }
}

/// <summary>Series chain of elements (AND semantics).</summary>
public sealed class SeriesNetwork : LadderNode
{
    /// <summary>Elements, evaluated left to right.</summary>
    public List<LadderNode> Elements { get; } = new();

    /// <summary>Appends an element and returns this network (fluent helper for programmatic builders such as the MCP layer).</summary>
    public SeriesNetwork Add(LadderNode node)
    {
        Elements.Add(node);
        return this;
    }
}

/// <summary>Parallel branch set (OR semantics across branches).</summary>
public sealed class ParallelNetwork : LadderNode
{
    /// <summary>Each branch is its own series chain.</summary>
    public List<SeriesNetwork> Branches { get; } = new();

    /// <summary>Appends a branch.</summary>
    public ParallelNetwork Add(SeriesNetwork branch)
    {
        Branches.Add(branch);
        return this;
    }
}

/// <summary>One rung of a program: a top-level series network plus an optional comment.</summary>
public sealed class Rung
{
    /// <summary>Rung comment (displayed above the rung).</summary>
    public string? Comment { get; set; }

    /// <summary>Root logic; must contain at least one element.</summary>
    public SeriesNetwork Logic { get; set; } = new();

    /// <summary>Editor layout hint (cell row/column). Purely cosmetic; the semantic model never depends on it.</summary>
    public int? LayoutRow { get; set; }

    /// <summary>Enumerates every element in evaluation order.</summary>
    public IEnumerable<LadderNode> Walk()
    {
        foreach (LadderNode n in Walk(Logic))
        {
            yield return n;
        }
    }

    private static IEnumerable<LadderNode> Walk(LadderNode node)
    {
        yield return node;
        switch (node)
        {
            case SeriesNetwork s:
                foreach (LadderNode child in s.Elements.SelectMany(Walk))
                {
                    yield return child;
                }

                break;
            case ParallelNetwork p:
                foreach (LadderNode child in p.Branches.SelectMany(Walk))
                {
                    yield return child;
                }

                break;
        }
    }
}
