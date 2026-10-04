using System.Text;
using DeltaStudio.Core.Program;

namespace DeltaStudio.Compiler.Il;

/// <summary>
/// Renders the ladder IR back to Delta-style IL text. Round-trips with <see cref="IlParser"/>:
/// single-contact parallel branches are emitted with OR, general parallel networks with LD-block + ORB/ANB.
/// </summary>
public sealed class IlFormatter
{
    /// <summary>Formats a whole program with "; Rung N" headers.</summary>
    public string Format(PlcProgram program)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < program.Main.Count; i++)
        {
            sb.AppendLine($"; Rung {i + 1}{(string.IsNullOrEmpty(program.Main[i].Comment) ? string.Empty : ": " + program.Main[i].Comment)}");
            FormatRung(sb, program.Main[i]);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Formats a single rung body.</summary>
    public string Format(Rung rung)
    {
        var sb = new StringBuilder();
        FormatRung(sb, rung);
        return sb.ToString();
    }

    private static void FormatRung(StringBuilder sb, Rung rung)
    {
        List<LadderNode> elements = rung.Logic.Elements;
        for (int i = 0; i < elements.Count; i++)
        {
            bool firstOfChain = i == 0;
            LadderNode node = elements[i];
            switch (node)
            {
                case ContactNode c:
                    sb.AppendLine($"{(firstOfChain ? Mnem("LD", c.Polarity) : Mnem("AND", c.Polarity))} {c.Device}");
                    break;
                case CoilNode k:
                    sb.AppendLine($"{(k.Action == CoilAction.Output ? "OUT" : k.Action == CoilAction.Set ? "SET" : "RST")} {k.Device}");
                    break;
                case InstructionNode n:
                    sb.AppendLine(n.Call.ToText());
                    break;
                case ParallelNetwork p when SingleContactBranches(p):
                {
                    // First branch replaces the base element; remaining branches use OR.
                    ContactNode head = (ContactNode)p.Branches[0].Elements[0];
                    sb.AppendLine($"{(firstOfChain ? Mnem("LD", head.Polarity) : Mnem("AND", head.Polarity))} {head.Device}");
                    for (int b = 1; b < p.Branches.Count; b++)
                    {
                        ContactNode branch = (ContactNode)p.Branches[b].Elements[0];
                        sb.AppendLine($"{Mnem("OR", branch.Polarity)} {branch.Device}");
                    }

                    break;
                }
                case ParallelNetwork p:
                {
                    // General form: each branch is an LD block; branches are ORB-merged, then ANB-into the prefix
                    // (only valid at chain start for ANB semantics; when i>0 the prefix is folded by ANB).
                    if (i > 0)
                    {
                        throw new NotSupportedException("Multi-element parallel branch mid-chain is not supported by the IL formatter; restructure the rung or use the editor IR directly.");
                    }

                    FormatBranchChain(sb, p.Branches[0]);
                    for (int b = 1; b < p.Branches.Count; b++)
                    {
                        FormatBranchChain(sb, p.Branches[b]);
                        sb.AppendLine("ORB");
                    }

                    break;
                }
                default:
                    throw new NotSupportedException($"cannot format {node.GetType().Name}");
            }
        }
    }

    private static void FormatBranchChain(StringBuilder sb, SeriesNetwork chain)
    {
        for (int i = 0; i < chain.Elements.Count; i++)
        {
            LadderNode node = chain.Elements[i];
            switch (node)
            {
                case ContactNode c:
                    sb.AppendLine($"{(i == 0 ? Mnem("LD", c.Polarity) : Mnem("AND", c.Polarity))} {c.Device}");
                    break;
                case CoilNode:
                case InstructionNode:
                    throw new NotSupportedException("coil/instruction inside a parallel branch is invalid ladder structure.");
                case ParallelNetwork:
                    throw new NotSupportedException("nested parallel inside a branch is not supported by the IL formatter.");
            }
        }
    }

    private static bool SingleContactBranches(ParallelNetwork p) =>
        p.Branches.Count >= 2 && p.Branches.All(b => b.Elements.Count == 1 && b.Elements[0] is ContactNode);

    private static string Mnem(string family, ContactPolarity polarity) => (family, polarity) switch
    {
        ("LD", ContactPolarity.NormallyOpen) => "LD",
        ("LD", ContactPolarity.NormallyClosed) => "LDI",
        ("LD", ContactPolarity.RisingEdge) => "LDP",
        ("LD", ContactPolarity.FallingEdge) => "LDF",
        ("AND", ContactPolarity.NormallyOpen) => "AND",
        ("AND", ContactPolarity.NormallyClosed) => "ANI",
        ("AND", ContactPolarity.RisingEdge) => "ANP",
        ("AND", ContactPolarity.FallingEdge) => "ANF",
        ("OR", ContactPolarity.NormallyOpen) => "OR",
        ("OR", ContactPolarity.NormallyClosed) => "ORI",
        ("OR", ContactPolarity.RisingEdge) => "ORP",
        ("OR", ContactPolarity.FallingEdge) => "ORF",
        _ => throw new NotSupportedException($"no IL mnemonic for {family}/{polarity}"),
    };
}
