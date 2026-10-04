using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Instructions;
using DeltaStudio.Core.Model;
using DeltaStudio.Core.Program;
using DeltaStudio.Core.Project;

namespace DeltaStudio.Core.Validation;

/// <summary>
/// Target-aware ladder/project validation. Deliberately pure and synchronous so the identical
/// rules run inside WinUI (live squiggles), the CLI, and every MCP validate/compile call.
/// </summary>
public sealed class ProjectValidator
{
    private readonly IInstructionCatalog _catalog;

    /// <summary>Creates a validator bound to a backend instruction catalog.</summary>
    public ProjectValidator(IInstructionCatalog catalog) => _catalog = catalog;

    /// <summary>Resolves the model definition for a project (backend hook; must never be null-capable downstream).</summary>
    public delegate PlcModelDefinition? ModelResolver(string modelId);

    /// <summary>Validates the whole project.</summary>
    public ValidationReport Validate(PlcProject project, PlcModelDefinition model)
    {
        var report = new ValidationReport();

        if (project.Program.Main.Count == 0)
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Warning, "DS1001",
                "Program is empty — nothing will execute on the CPU."));
        }

        for (int i = 0; i < project.Program.Main.Count; i++)
        {
            ValidateRung(project.Program.Main[i], i, model, report);
        }

        ValidateCoilReuse(project.Program.Main, report);
        ValidateSymbols(project.Symbols, model, report);
        ValidateCapacity(project.Program, model, report);

        return report;
    }

    private void ValidateRung(Rung rung, int index, PlcModelDefinition model, ValidationReport report)
    {
        if (rung.Logic.Elements.Count == 0)
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1010", "Rung has no logic elements.", index));
            return;
        }

        ValidateSeries(rung.Logic, index, model, report, "R");

        if (!rung.Logic.Elements.Any(e => e is CoilNode or InstructionNode))
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Warning, "DS1011",
                "Rung evaluates conditions but drives nothing (no coil or instruction block).", index));
        }
    }

    private void ValidateSeries(SeriesNetwork series, int rung, PlcModelDefinition model, ValidationReport report, string path)
    {
        for (int i = 0; i < series.Elements.Count; i++)
        {
            string elementPath = $"{path}/{series.Elements[i].GetType().Name[^5..].ToLowerInvariant()}{i}";
            switch (series.Elements[i])
            {
                case ContactNode c:
                    ValidateAddress(c.Device, rung, model, report, elementPath, asWrite: false);
                    break;
                case CoilNode k:
                    ValidateAddress(k.Device, rung, model, report, elementPath, asWrite: true);
                    if (k.Device.Kind is DeviceKind.T or DeviceKind.C)
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1005",
                            $"OUT/SET/RST on {k.Device} is invalid; use the TMR/CTR instruction blocks for timers and counters.",
                            rung, elementPath));
                    }

                    break;
                case InstructionNode n:
                    ValidateCall(n.Call, rung, model, report, elementPath);
                    break;
                case ParallelNetwork p:
                    for (int b = 0; b < p.Branches.Count; b++)
                    {
                        if (p.Branches[b].Elements.Count == 0)
                        {
                            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1012",
                                "Parallel branch is empty (a short-circuit branch is not allowed).",
                                rung, $"{elementPath}/B{b}"));
                        }

                        ValidateSeries(p.Branches[b], rung, model, report, $"{elementPath}/B{b}");
                    }

                    break;
                case SeriesNetwork s:
                    ValidateSeries(s, rung, model, report, elementPath);
                    break;
            }
        }
    }

    private void ValidateAddress(DeviceAddress address, int rung, PlcModelDefinition model, ValidationReport report, string path, bool asWrite)
    {
        if (!model.TryGetDevice(address.Kind, out DeviceCapabilities? caps) || caps is null)
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1002",
                $"Device class {address.Kind} is not supported by {model.Id} (used: {address}).", rung, path));
            return;
        }

        if (!caps.Contains(address))
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1003",
                $"{address} is outside the range supported by {model.Id} ({string.Join(", ", caps.Ranges)}).", rung, path));
            return;
        }

        if (asWrite && caps.Access == DeviceAccess.ReadOnly)
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1004",
                $"{address} is read-only ({address.Kind} reflects physical inputs); it cannot be driven by a coil.", rung, path));
        }
    }

    private void ValidateCall(InstructionCall call, int rung, PlcModelDefinition model, ValidationReport report, string path)
    {
        if (!_catalog.TryGet(call.Mnemonic, out InstructionDefinition? def) || def is null)
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1006",
                $"'{call.Mnemonic}' is not a known instruction for backend '{_catalog.Backend}'.", rung, path));
            return;
        }

        if (def.SupportedFamilies is not null && !def.SupportedFamilies.Contains(model.Family, StringComparer.OrdinalIgnoreCase))
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1007",
                $"'{def.Mnemonic}' is not supported on family {model.Family}.", rung, path));
        }

        int required = def.Operands.Count(o => !o.Optional);
        if (call.Operands.Count < required || call.Operands.Count > def.Operands.Count)
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1008",
                $"{def.Mnemonic} expects {required}..{def.Operands.Count} operand(s), got {call.Operands.Count} ('{call.ToText()}').",
                rung, path));
            return;
        }

        for (int i = 0; i < call.Operands.Count; i++)
        {
            ValidateOperand(def, i, call.Operands[i], rung, model, report, $"{path}/{def.Mnemonic}.op{i}");
        }

        if (!def.EncodingVerified)
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Info, "DS1090",
                $"{def.Mnemonic}: semantics transcribed from the Delta manual, machine encoding UNVERIFIED — " +
                "compilation stops at the assembly listing stage by design.", rung, path));
        }
    }

    private void ValidateOperand(
        InstructionDefinition def, int slot, Operand operand, int rung,
        PlcModelDefinition model, ValidationReport report, string path)
    {
        OperandSpec spec = def.Operands[slot];
        switch (spec.Constraint)
        {
            case OperandConstraint.Constant when operand is not ConstantOperand:
                report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1009",
                    $"{def.Mnemonic} operand '{spec.Name}' must be a K/H constant.", rung, path));
                break;
            case OperandConstraint.BitDevice or OperandConstraint.WordDevice
                or OperandConstraint.AnyDevice or OperandConstraint.Timer or OperandConstraint.Counter
                when operand is not DeviceOperand:
                report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1009",
                    $"{def.Mnemonic} operand '{spec.Name}' must be a device address.", rung, path));
                break;
        }

        if (operand is DeviceOperand dev)
        {
            switch (spec.Constraint)
            {
                case OperandConstraint.BitDevice when dev.Address.Kind is not (DeviceKind.X or DeviceKind.Y or DeviceKind.M or DeviceKind.S or DeviceKind.L):
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1009",
                        $"{def.Mnemonic} operand '{spec.Name}' must be a bit device (X/Y/M/S/L), got {dev.Address}.", rung, path));
                    break;
                case OperandConstraint.WordDevice when dev.Address.Kind is not (DeviceKind.D or DeviceKind.T or DeviceKind.C):
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1009",
                        $"{def.Mnemonic} operand '{spec.Name}' must be a word device (D/T/C), got {dev.Address}.", rung, path));
                    break;
                case OperandConstraint.Timer when dev.Address.Kind != DeviceKind.T:
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1009",
                        $"{def.Mnemonic} operand '{spec.Name}' must be a timer (Tnn).", rung, path));
                    break;
                case OperandConstraint.Counter when dev.Address.Kind != DeviceKind.C:
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1009",
                        $"{def.Mnemonic} operand '{spec.Name}' must be a counter (Cnn).", rung, path));
                    break;
            }

            ValidateAddress(dev.Address, rung, model, report, path, asWrite: false);
        }

        if (operand is ConstantOperand c)
        {
            long lo = spec.Constraint == OperandConstraint.DeviceOrConstant32 ? -2147483648L : -32768L;
            long hi = spec.Constraint == OperandConstraint.DeviceOrConstant32 ? 2147483647L : 65535L;
            if (c.Value < lo || c.Value > hi)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1013",
                    $"{def.Mnemonic} constant {c.ToText()} outside 16-bit range (use a D instruction/32-bit operand for large values).",
                    rung, path));
            }
        }
    }

    private static void ValidateCoilReuse(IReadOnlyList<Rung> rungs, ValidationReport report)
    {
        var firstWrite = new Dictionary<DeviceAddress, int>();
        for (int i = 0; i < rungs.Count; i++)
        {
            foreach (CoilNode coil in rungs[i].Walk().OfType<CoilNode>().Where(c => c.Action == CoilAction.Output))
            {
                if (firstWrite.TryGetValue(coil.Device, out int other) && other != i)
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Warning, "DS1014",
                        $"Coil {coil.Device} is written by both rung {other + 1} and rung {i + 1}; the last scan write wins. " +
                        "Consider SET/RST or merging the logic.", i, coil.Device.ToString()));
                }
                else
                {
                    firstWrite[coil.Device] = i;
                }
            }
        }
    }

    private static void ValidateSymbols(IReadOnlyList<SymbolDefinition> symbols, PlcModelDefinition model, ValidationReport report)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SymbolDefinition s in symbols)
        {
            if (!seen.Add(s.Name))
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS2001", $"Duplicate symbol name '{s.Name}'."));
            }

            if (!model.TryGetDevice(s.Address.Kind, out DeviceCapabilities? caps) || caps is null || !caps.Contains(s.Address))
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS2002",
                    $"Symbol '{s.Name}' points at {s.Address}, outside {model.Id} ranges."));
            }
        }
    }

    private static void ValidateCapacity(PlcProgram program, PlcModelDefinition model, ValidationReport report)
    {
        // Conservative step estimate: each contact/coil node = 1 step (LD=1, AND=1, ANB≈1, OUT=1);
        // application instructions are excluded because their real cost is unverified per model.
        int estimate = program.Main.Sum(r => r.Walk().Count(n => n is ContactNode or CoilNode));
        if (estimate > model.ProgramCapacitySteps)
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Error, "DS1015",
                $"Program uses ~{estimate} basic steps, exceeding {model.Id}'s {model.ProgramCapacitySteps}-step capacity."));
        }
        else if (estimate > (int)(model.ProgramCapacitySteps * 0.9))
        {
            report.Add(new Diagnostic(DiagnosticSeverity.Warning, "DS1016",
                $"Program is at ~{estimate}/{model.ProgramCapacitySteps} steps (>90% of capacity)."));
        }
    }
}
