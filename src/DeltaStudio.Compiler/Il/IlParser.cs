using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Program;

namespace DeltaStudio.Compiler.Il;

/// <summary>Thrown for malformed IL text; carries line number for editor integration.</summary>
public sealed class IlSyntaxException(string message, int line) : FormatException($"{message} (line {line})")
{
    /// <summary>1-based line number where the error was detected.</summary>
    public int Line { get; } = line;
}

/// <summary>
/// Parses Delta-style instruction lists into the generic ladder IR and back (see IlFormatter).
/// Supported: the F0–F13 style terminal logic (LD/LDI/LDP/LDF, AND/OR families), block combinators
/// ANB/ORB, coils OUT/SET/RST, and application instruction lines ("MOV D0 K100", "TMR T0 K50").
/// This is a textual representation of the IR, not a claim of WPLSoft IL import compatibility.
/// </summary>
public sealed class IlParser
{
    private static readonly HashSet<string> LdStarts = new(StringComparer.OrdinalIgnoreCase) { "LD", "LDI", "LDP", "LDF" };
    private static readonly HashSet<string> AndStarts = new(StringComparer.OrdinalIgnoreCase) { "AND", "ANI", "ANP", "ANF" };
    private static readonly HashSet<string> OrStarts = new(StringComparer.OrdinalIgnoreCase) { "OR", "ORI", "ORP", "ORF" };
    private static readonly HashSet<string> CoilStarts = new(StringComparer.OrdinalIgnoreCase) { "OUT", "SET", "RST" };

    /// <summary>Parses multiple rungs separated by blank/comment lines or consecutive LD blocks.</summary>
    public PlcProgram ParseProgram(string ilText)
    {
        var program = new PlcProgram();

        // Split input into "rung texts" at blank lines; also split when a new LD follows a coil.
        string[] lines = ilText.Replace("\r\n", "\n").Split('\n');
        var current = new List<string>();
        bool closedCoil = false;

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
            {
                if (current.Count > 0)
                {
                    program.Main.Add(ParseRung(current));
                    current.Clear();
                    closedCoil = false;
                }

                continue;
            }

            string op = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
            bool isStart = LdStarts.Contains(op);
            bool isCoil = CoilStarts.Contains(op);

            if (isStart && closedCoil)
            {
                program.Main.Add(ParseRung(current));
                current.Clear();
                closedCoil = false;
            }

            current.Add(line);
            if (isCoil)
            {
                closedCoil = true;
            }
        }

        if (current.Count > 0)
        {
            program.Main.Add(ParseRung(current));
        }

        return program;
    }

    /// <summary>Parses one rung from IL lines.</summary>
    public Rung ParseRung(IEnumerable<string> lines)
    {
        var stack = new List<SeriesNetwork>();
        var rung = new Rung();
        int lineNumber = 0;

        foreach (string line in lines)
        {
            lineNumber++;
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string op = tokens[0].ToUpperInvariant();

            switch (op)
            {
                case var s when LdStarts.Contains(s):
                {
                    Require(tokens, 2, op, lineNumber);
                    SeriesNetwork chain = new();
                    chain.Add(new ContactNode { Device = ParseDevice(tokens[1], lineNumber), Polarity = ContactPolarityOf(op, 0, lineNumber) });
                    stack.Add(chain);
                    break;
                }
                case var s when AndStarts.Contains(s):
                {
                    Require(tokens, 2, op, lineNumber);
                    if (stack.Count == 0)
                    {
                        throw new IlSyntaxException($"'{op}' without an open LD block", lineNumber);
                    }

                    SeriesNetwork top = stack[^1];
                    if (top.Elements.Count == 0)
                    {
                        throw new IlSyntaxException($"'{op}' cannot start a block", lineNumber);
                    }

                    top.Add(new ContactNode { Device = ParseDevice(tokens[1], lineNumber), Polarity = ContactPolarityOf(op, 1, lineNumber) });
                    break;
                }
                case var s when OrStarts.Contains(s):
                {
                    Require(tokens, 2, op, lineNumber);
                    if (stack.Count == 0)
                    {
                        throw new IlSyntaxException($"'{op}' without an open LD block", lineNumber);
                    }

                    SeriesNetwork top = stack[^1];
                    ContactNode newContact = new() { Device = ParseDevice(tokens[1], lineNumber), Polarity = ContactPolarityOf(op, 1, lineNumber) };
                    AppendParallelContact(top, newContact, lineNumber);
                    break;
                }
                case "ANB":
                {
                    if (stack.Count < 2)
                    {
                        throw new IlSyntaxException("'ANB' needs two open blocks", lineNumber);
                    }

                    SeriesNetwork right = stack[^1];
                    SeriesNetwork left = stack[^2];
                    stack.RemoveAt(stack.Count - 1);
                    var merged = new SeriesNetwork();
                    merged.Elements.AddRange(left.Elements);
                    merged.Elements.AddRange(right.Elements);
                    stack[^1] = merged;
                    break;
                }
                case "ORB":
                {
                    if (stack.Count < 2)
                    {
                        throw new IlSyntaxException("'ORB' needs two open blocks", lineNumber);
                    }

                    SeriesNetwork orRight = stack[^1];
                    SeriesNetwork orLeft = stack[^2];
                    stack.RemoveAt(stack.Count - 1);
                    var parallel = new ParallelNetwork();
                    parallel.Branches.Add(orLeft);
                    parallel.Branches.Add(orRight);
                    stack[^1] = new SeriesNetwork { Elements = { parallel } };
                    break;
                }
                case var c when CoilStarts.Contains(c):
                {
                    Require(tokens, 2, op, lineNumber);
                    if (stack.Count == 0)
                    {
                        throw new IlSyntaxException($"'{op}' without logic", lineNumber);
                    }

                    stack[^1].Add(new CoilNode
                    {
                        Device = ParseDevice(tokens[1], lineNumber),
                        Action = c switch { "OUT" => CoilAction.Output, "SET" => CoilAction.Set, _ => CoilAction.Reset },
                    });
                    break;
                }
                default:
                {
                    // Application instruction line: MNEMONIC operand operand ...
                    if (stack.Count == 0)
                    {
                        throw new IlSyntaxException($"'{op}' without a preceding LD block", lineNumber);
                    }

                    var call = new InstructionCall { Mnemonic = op };
                    foreach (string t in tokens[1..])
                    {
                        if (!Operand.TryParse(t, out Operand? operand, out string? err))
                        {
                            throw new IlSyntaxException(err ?? $"bad operand '{t}'", lineNumber);
                        }

                        call.Operands.Add(operand!);
                    }

                    stack[^1].Add(new InstructionNode { Call = call });
                    break;
                }
            }
        }

        if (stack.Count == 0)
        {
            throw new IlSyntaxException("empty rung", lineNumber);
        }

        if (stack.Count != 1)
        {
            throw new IlSyntaxException($"{stack.Count - 1} block(s) left open (missing coil/ANB/ORB)", lineNumber);
        }

        rung.Logic = stack[0];
        return rung;
    }

    /// <summary>OR semantics on a chain: branch against the last element, or into an existing parallel.</summary>
    private static void AppendParallelContact(SeriesNetwork chain, ContactNode contact, int lineNumber)
    {
        if (chain.Elements.Count == 0)
        {
            throw new IlSyntaxException("'OR' cannot start a block", lineNumber);
        }

        LadderNode last = chain.Elements[^1];
        if (last is ParallelNetwork existing)
        {
            if (existing.Branches.Count == 1 && existing.Branches[0].Elements.Count > 1)
            {
                // OR of a full preceding block that was just ORB-ed: append single branch
                existing.Branches.Add(new SeriesNetwork { Elements = { contact } });
                return;
            }

            existing.Branches.Add(new SeriesNetwork { Elements = { contact } });
            return;
        }

        if (last is ContactNode)
        {
            var p = new ParallelNetwork();
            p.Branches.Add(new SeriesNetwork { Elements = { last } });
            p.Branches.Add(new SeriesNetwork { Elements = { contact } });
            chain.Elements[^1] = p;
            return;
        }

        throw new IlSyntaxException($"cannot OR onto a {last.GetType().Name}", lineNumber);
    }

    private static ContactPolarity ContactPolarityOf(string op, int suffixStart, int lineNumber) => op.ToUpperInvariant() switch
    {
        "LD" or "AND" or "OR" => ContactPolarity.NormallyOpen,
        "LDI" or "ANI" or "ORI" => ContactPolarity.NormallyClosed,
        "LDP" or "ANP" or "ORP" => ContactPolarity.RisingEdge,
        "LDF" or "ANF" or "ORF" => ContactPolarity.FallingEdge,
        _ => throw new IlSyntaxException($"unknown contact form '{op}'", lineNumber),
    };

    private static DeviceAddress ParseDevice(string text, int lineNumber)
    {
        if (!DeviceAddress.TryParse(text, out DeviceAddress a, out string? err))
        {
            throw new IlSyntaxException(err ?? $"bad device '{text}'", lineNumber);
        }

        return a;
    }

    private static void Require(string[] tokens, int count, string op, int line)
    {
        if (tokens.Length < count)
        {
            throw new IlSyntaxException($"'{op}' is missing an operand", line);
        }
    }
}
