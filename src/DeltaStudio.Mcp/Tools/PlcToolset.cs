using System.Text.Json.Nodes;
using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Model;
using DeltaStudio.Core.Program;
using DeltaStudio.Core.Project;
using DeltaStudio.Core.Common;
using DeltaStudio.Delta.Comm;
using DeltaStudio.Infrastructure.Monitoring;
using DeltaStudio.Infrastructure;
using DeltaStudio.Infrastructure.Persistence;
using DeltaStudio.Infrastructure.Safety;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Mock;

namespace DeltaStudio.Mcp.Tools;

/// <summary>
/// The DeltaStudio PLC tool surface. Every tool is a thin adapter over <see cref="DeltaStudioEngine"/>;
/// there is no second copy of any business logic. Safety classes are declared per tool and the
/// DANGEROUS handlers verify a one-shot confirmation token issued by safety_prepare_write.
/// </summary>
public static class PlcToolset
{
    private const string Refusal =
        "This operation can change live machine behaviour. Two-step confirmation is required: " +
        "call safety_prepare_write with the same arguments, present the returned plan to the human operator, " +
        "and only after approval repeat this call with confirmation_token. Silent writes are refused by design (docs/safety.md).";

    /// <summary>Builds the complete registry for an engine host.</summary>
    public static ToolRegistry Build()
    {
        var reg = new ToolRegistry();

        // ==================== project ====================
        reg.Add(new McpTool(
            "project_create",
            "Create a new empty PLC project in memory (unsaved). model must be one of the ids from plc_get_available_models.",
            SchemaBuilder.Object("Create project",
                SchemaBuilder.String("name", "Project name", true),
                SchemaBuilder.String("model", "Target model id (e.g. DVP14SS2T)", true),
                SchemaBuilder.String("directory", "Optional directory to create and save into immediately")),
            SafetyClass.ProjectWrite,
            async (ctx, args) =>
            {
                try
                {
                    string name = SchemaBuilder.Str(args, "name", "MCP-Project")!;
                    string model = SchemaBuilder.Str(args, "model", "")!;
                    PlcModelDefinition m = ProjectWorkspace.ResolveModel(model);
                    ctx.Engine.Workspace.NewProject(name, m.Id);
                    string? dir = SchemaBuilder.Str(args, "directory");
                    if (dir is not null)
                    {
                        await ctx.Engine.Workspace.SaveAsync(dir, ctx.CancellationToken);
                        await ctx.Engine.Registry.RegisterAsync(dir, name, m.Id, ctx.AgentId, ctx.CancellationToken);
                        return ToolResult.Text($"Created project '{name}' ({m.Id}) in {dir}.");
                    }

                    return ToolResult.Text($"Created project '{name}' in memory targeting {m.Id} ({m.DisplayName}). Save with project_save.");
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "project_open",
            "Open a DeltaStudio project directory (project.json + program/main.json + symbols.json).",
            SchemaBuilder.Object("Open project", SchemaBuilder.String("path", "Directory path", true)),
            SafetyClass.Safe,
            async (ctx, args) =>
            {
                try
                {
                    string path = SchemaBuilder.Str(args, "path", "")!;
                    PlcProject p = await ctx.Engine.Workspace.OpenAsync(path, ctx.CancellationToken);
                    await ctx.Engine.Registry.RegisterAsync(path, p.Metadata.Name, p.Target.ModelId, ctx.AgentId, ctx.CancellationToken);
                    return ToolResult.Text($"Opened project '{p.Metadata.Name}' from {path}: {p.Program.Main.Count} rungs, target {p.Target.ModelId}.");
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "project_save",
            "Save the current project (to its directory, or a new one). A rollback checkpoint is recorded automatically.",
            SchemaBuilder.Object("Save project", SchemaBuilder.String("path", "Optional new directory")),
            SafetyClass.ProjectWrite,
            async (ctx, args) =>
            {
                try
                {
                    PlcProject p = RequireProject(ctx);
                    string? path = SchemaBuilder.Str(args, "path");
                    string dir0 = path ?? ctx.Engine.Workspace.Directory
                        ?? throw new InvalidOperationException("First save must include 'path' (target directory).");
                    await ctx.Engine.Registry.AddCheckpointAsync(
                        dir0, ctx.AgentId, "pre-save", JsonProjectSerializer.Serialize(p), ctx.CancellationToken);
                    await ctx.Engine.Workspace.SaveAsync(path, ctx.CancellationToken);
                    string dir = ctx.Engine.Workspace.Directory!;
                    await ctx.Engine.Registry.RegisterAsync(dir, p.Metadata.Name, p.Target.ModelId, ctx.AgentId, ctx.CancellationToken);
                    return ToolResult.Text($"Saved project to {dir}.");
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "project_close",
            "Close the current project (refuses when dirty unless force=true).",
            SchemaBuilder.Object("Close project", SchemaBuilder.Bool("force", "Discard unsaved changes", false)),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                if (ctx.Engine.Workspace.IsDirty && !SchemaBuilder.BoolArg(args, "force"))
                {
                    return Task.FromResult(ToolResult.Text("Project has unsaved changes; pass force=true to close anyway or project_save first.", true));
                }

                ctx.Engine.Workspace.Close();
                return Task.FromResult(ToolResult.Text("Project closed."));
            }));

        reg.Add(new McpTool(
            "project_get_info",
            "Project metadata, target, rung count and dirty flag as JSON.",
            SchemaBuilder.Object("Project info"),
            SafetyClass.Safe,
            (ctx, _) =>
            {
                PlcProject? p = ctx.Engine.Workspace.Current;
                if (p is null)
                {
                    return Task.FromResult(ToolResult.Text("No project open.", true));
                }

                var info = new JsonObject
                {
                    ["name"] = p.Metadata.Name,
                    ["description"] = p.Metadata.Description,
                    ["author"] = p.Metadata.Author,
                    ["model"] = p.Target.ModelId,
                    ["rungs"] = p.Program.Main.Count,
                    ["symbols"] = p.Symbols.Count,
                    ["dirty"] = ctx.Engine.Workspace.IsDirty,
                    ["directory"] = ctx.Engine.Workspace.Directory,
                    ["modifiedUtc"] = p.Metadata.ModifiedUtc.ToString("O"),
                    ["createdUtc"] = p.Metadata.CreatedUtc.ToString("O"),
                };
                return Task.FromResult(ToolResult.Json(info));
            }));

        // ==================== model / capabilities ====================
        reg.Add(new McpTool(
            "plc_get_available_models",
            "List all built-in PLC model ids.",
            SchemaBuilder.Object("List models"),
            SafetyClass.Safe,
            (ctx, _) => Task.FromResult(ToolResult.Json(
                new JsonArray(ctx.Engine.Models.Keys.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray())))));

        reg.Add(new McpTool(
            "plc_select_model",
            "Change the target CPU of the open project.",
            SchemaBuilder.Object("Select model", SchemaBuilder.String("model", "Model id", true)),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    string model = SchemaBuilder.Str(args, "model", "")!;
                    ctx.Engine.Workspace.SelectModel(model);
                    return Task.FromResult(ToolResult.Text($"Target set to {model}."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "plc_get_capabilities",
            "Full capability record for a model: device ranges, capacities, verification status. Omit model to use the open project's target.",
            SchemaBuilder.Object("Capabilities", SchemaBuilder.String("model", "Model id (optional)")),
            SafetyClass.Safe,
            (ctx, args) =>
            {
                try
                {
                    PlcModelDefinition m = ResolveModel(ctx, SchemaBuilder.Str(args, "model"));
                    return Task.FromResult(ToolResult.Text(CapabilitiesText(m)));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        // ==================== program ====================
        reg.Add(new McpTool(
            "program_get",
            "The whole program as JSON IR ({ schemaVersion, rungs:[ {number, comment, logic} ] }).",
            SchemaBuilder.Object("Get program"),
            SafetyClass.Safe,
            (ctx, _) =>
            {
                PlcProject p = RequireProject(ctx);
                return Task.FromResult(ToolResult.Text(PlcProjectJson.SerializeProgram(p.Program)));
            }));

        reg.Add(new McpTool(
            "program_set",
            "Replace the entire program from JSON IR (same shape as program_get). Prefer rung_*/ladder_* tools for incremental edits.",
            SchemaBuilder.Object("Set program", SchemaBuilder.String("programJson", "JSON IR program", true)),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    string json = SchemaBuilder.Str(args, "programJson", "")!;
                    PlcProgram program = PlcProjectJson.DeserializeProgram(json);
                    ctx.Engine.Workspace.Mutate(p => ReplaceProgram(p, program), "program_set");
                    return Task.FromResult(ToolResult.Text($"Program replaced: {program.Main.Count} rungs."));
                }
                catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or NotSupportedException or ArgumentException)
                {
                    return Task.FromResult(ToolResult.Text("Invalid program JSON: " + e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "rung_create",
            "Append (or insert at index) an empty rung, optionally with a comment. Returns the 0-based index.",
            SchemaBuilder.Object("Create rung",
                SchemaBuilder.Int("index", "Insert position (default: append)"),
                SchemaBuilder.String("comment", "Rung comment")),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                int? index = SchemaBuilder.IntArg(args, "index");
                string? comment = SchemaBuilder.Str(args, "comment");
                int at = ctx.Engine.Workspace.Mutate(p =>
                {
                    p.Program.AddRung(comment);
                    int last = p.Program.Main.Count - 1;
                    if (index is int i && i >= 0 && i < last)
                    {
                        p.Program.MoveRung(last, i);
                        return i;
                    }

                    return last;
                }, "rung_create");
                return Task.FromResult(ToolResult.Text($"Rung created at index {at} (display rung {at + 1})."));
            }));

        reg.Add(new McpTool(
            "rung_get",
            "One rung as JSON ({number, comment, logic}), logic is the node JSON.",
            SchemaBuilder.Object("Get rung", SchemaBuilder.Int("rung", "0-based rung index", true)),
            SafetyClass.Safe,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    Rung r = RungAt(RequireProject(ctx), index);
                    var dto = new JsonObject
                    {
                        ["number"] = index + 1,
                        ["comment"] = r.Comment,
                        ["logic"] = JsonNode.Parse(PlcProjectJson.SerializeNode(r.Logic)),
                    };
                    return Task.FromResult(ToolResult.Json(dto));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "rung_update",
            "Update rung comment and/or replace its logic wholesale (logicJson = series node JSON).",
            SchemaBuilder.Object("Update rung",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.String("comment", "New comment"),
                SchemaBuilder.String("logicJson", "Replacement series-node JSON")),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    string? comment = SchemaBuilder.Str(args, "comment");
                    string? logicJson = SchemaBuilder.Str(args, "logicJson");
                    ctx.Engine.Workspace.Mutate(p =>
                    {
                        Rung r = RungAt(p, index);
                        if (comment is not null)
                        {
                            r.Comment = comment;
                        }

                        if (logicJson is not null)
                        {
                            if (PlcProjectJson.DeserializeNode(logicJson) is not SeriesNetwork sn)
                            {
                                throw new ArgumentException("logicJson must be a series node.");
                            }

                            r.Logic = sn;
                        }
                    }, "rung_update");
                    return Task.FromResult(ToolResult.Text($"Rung {index + 1} updated."));
                }
                catch (Exception e) when (e is FormatException or ArgumentException or System.Text.Json.JsonException or ArgumentOutOfRangeException)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "rung_delete",
            "Delete a rung by index.",
            SchemaBuilder.Object("Delete rung", SchemaBuilder.Int("rung", "0-based index", true)),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    ctx.Engine.Workspace.Mutate(p =>
                    {
                        if (!p.Program.RemoveRung(index))
                        {
                            throw new ArgumentOutOfRangeException("rung", $"Rung {index} does not exist (count {p.Program.Main.Count}).");
                        }
                    }, "rung_delete");
                    return Task.FromResult(ToolResult.Text($"Rung {index + 1} deleted."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "rung_move",
            "Reorder rungs (move a rung to a new index).",
            SchemaBuilder.Object("Move rung",
                SchemaBuilder.Int("from", "Source index", true),
                SchemaBuilder.Int("to", "Destination index", true)),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int from = RequireInt(args, "from");
                    int to = RequireInt(args, "to");
                    ctx.Engine.Workspace.Mutate(p => p.Program.MoveRung(from, to), "rung_move");
                    return Task.FromResult(ToolResult.Text($"Rung {from + 1} moved to position {to + 1}."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        // ==================== ladder elements ====================
        reg.Add(new McpTool(
            "ladder_add_contact",
            "Add a contact (no/nc/rising/falling) in series; parallel=true ORs it with the last element (holding-circuit pattern).",
            SchemaBuilder.Object("Add contact",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.String("device", "Bit device address (X0, X17, M100...)", true),
                SchemaBuilder.Enum("polarity", "Contact polarity", false, "no", "nc", "rising", "falling"),
                SchemaBuilder.Bool("parallel", "True = OR branch on the last element", false)),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    DeviceAddress device = DeviceAddress.Parse(SchemaBuilder.Str(args, "device", "")!);
                    ContactNode contact = new()
                    {
                        Device = device,
                        Polarity = ParsePolarity(SchemaBuilder.Str(args, "polarity")),
                    };
                    bool parallel = SchemaBuilder.BoolArg(args, "parallel");
                    ctx.Engine.Workspace.Mutate(p =>
                    {
                        Rung rung = RungAt(p, index);
                        if (parallel)
                        {
                            AddParallelContact(rung, contact);
                        }
                        else
                        {
                            rung.Logic.Add(contact);
                        }
                    }, "ladder_add_contact");
                    return Task.FromResult(ToolResult.Text($"Contact {device} ({SchemaBuilder.Str(args, "polarity", "no")}) added to rung {index + 1}{(parallel ? " as parallel branch" : string.Empty)}."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "ladder_add_coil",
            "Add a coil (out/set/rst) at the end of the rung.",
            SchemaBuilder.Object("Add coil",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.String("device", "Bit device address", true),
                SchemaBuilder.Enum("action", "Coil action", false, "out", "set", "rst")),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    DeviceAddress device = DeviceAddress.Parse(SchemaBuilder.Str(args, "device", "")!);
                    CoilAction action = SchemaBuilder.Str(args, "action")?.ToLowerInvariant() switch
                    {
                        "set" => CoilAction.Set,
                        "rst" => CoilAction.Reset,
                        _ => CoilAction.Output,
                    };
                    ctx.Engine.Workspace.Mutate(p => RungAt(p, index).Logic.Add(new CoilNode { Device = device, Action = action }), "ladder_add_coil");
                    return Task.FromResult(ToolResult.Text($"Coil {action.ToString().ToUpperInvariant()} {device} added to rung {index + 1}."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "ladder_add_branch",
            "Parallel structure: simple form ORs 'device' with the rung's last element; full form replaces the last element with a parallel of " +
            "[existing-last, ...branchesJson] where branchesJson is a JSON array string of series-node JSON.",
            SchemaBuilder.Object("Add branch",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.String("device", "Simple form: contact device to OR with the last element"),
                SchemaBuilder.Enum("polarity", "Simple form polarity", false, "no", "nc", "rising", "falling"),
                SchemaBuilder.String("branchesJson", "Full form: JSON array of series-node JSON strings")),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    string? branchesJson = SchemaBuilder.Str(args, "branchesJson");
                    string? device = SchemaBuilder.Str(args, "device");
                    if (branchesJson is null && device is null)
                    {
                        return Task.FromResult(ToolResult.Text("Provide device (simple) or branchesJson (full).", true));
                    }

                    ctx.Engine.Workspace.Mutate(p =>
                    {
                        Rung rung = RungAt(p, index);
                        if (device is not null)
                        {
                            AddParallelContact(rung, new ContactNode
                            {
                                Device = DeviceAddress.Parse(device),
                                Polarity = ParsePolarity(SchemaBuilder.Str(args, "polarity")),
                            });
                            return;
                        }

                        if (rung.Logic.Elements.Count == 0)
                        {
                            throw new InvalidOperationException("Cannot parallel onto an empty rung.");
                        }

                        var arr = JsonNode.Parse(branchesJson!)!.AsArray();
                        var par = new ParallelNetwork();
                        LadderNode last = rung.Logic.Elements[^1];
                        par.Branches.Add(new SeriesNetwork { Elements = { last } });
                        foreach (JsonNode? b in arr)
                        {
                            if (PlcProjectJson.DeserializeNode(b!.ToJsonString()) is not SeriesNetwork sn)
                            {
                                throw new ArgumentException("Each branch must be a series node JSON.");
                            }

                            par.Branches.Add(sn);
                        }

                        rung.Logic.Elements[^1] = par;
                    }, "ladder_add_branch");
                    return Task.FromResult(ToolResult.Text($"Branch structure added to rung {index + 1}."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "ladder_add_timer",
            "Add a TMR timer block (Tn + preset). Optional leading contact is prepended (self-contained rung).",
            SchemaBuilder.Object("Add timer",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.String("timer", "Timer device (T0...)", true),
                SchemaBuilder.String("preset", "Preset K value or D device", true),
                SchemaBuilder.String("contact", "Optional leading contact device"),
                SchemaBuilder.Enum("contactPolarity", "Optional contact polarity", false, "no", "nc")),
            SafetyClass.ProjectWrite,
            (ctx, args) => AddTimerCounter(ctx, args, "TMR", "timer")));

        reg.Add(new McpTool(
            "ladder_add_counter",
            "Add a CTR counter block (Cn + preset). Optional leading contact is prepended (self-contained rung).",
            SchemaBuilder.Object("Add counter",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.String("counter", "Counter device (C0...)", true),
                SchemaBuilder.String("preset", "Preset K value or D device", true),
                SchemaBuilder.String("contact", "Optional leading contact device"),
                SchemaBuilder.Enum("contactPolarity", "Optional contact polarity", false, "no", "nc")),
            SafetyClass.ProjectWrite,
            (ctx, args) => AddTimerCounter(ctx, args, "CTR", "counter")));

        reg.Add(new McpTool(
            "instruction_add",
            "Add a catalog application instruction block to a rung. Unknown mnemonics are REFUSED (the catalog is the source of truth).",
            SchemaBuilder.Object("Add instruction",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.String("mnemonic", "Instruction mnemonic (see plc_get_instructions)", true),
                SchemaBuilder.StringArray("operands", "Operands as strings, e.g. [\"D0\",\"K100\"]")),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    string mnemonic = SchemaBuilder.Str(args, "mnemonic", "")!.Trim().ToUpperInvariant();
                    IReadOnlyList<string> operands = SchemaBuilder.StrArray(args, "operands");
                    if (!ctx.Engine.Catalog.TryGet(mnemonic, out Core.Instructions.InstructionDefinition? def) || def is null)
                    {
                        return Task.FromResult(ToolResult.Text(
                            $"Unknown instruction '{mnemonic}' for backend {ctx.Engine.Catalog.Backend}. " +
                            "DeltaStudio refuses to invent instructions — call plc_get_instructions for the supported list.", true));
                    }

                    var call = new InstructionCall { Mnemonic = mnemonic };
                    foreach (string op in operands)
                    {
                        call.Operands.Add(Operand.Parse(op));
                    }

                    ctx.Engine.Workspace.Mutate(p => RungAt(p, index).Logic.Add(new InstructionNode { Call = call }), "instruction_add");
                    return Task.FromResult(ToolResult.Text($"{call.ToText()} added to rung {index + 1} ({def.Summary})."));
                }
                catch (FormatException e)
                {
                    return Task.FromResult(ToolResult.Text("Invalid operand: " + e.Message, true));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "instruction_update",
            "Replace an instruction block in the rung's top-level chain (nodeIndex selects the element).",
            SchemaBuilder.Object("Update instruction",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.Int("nodeIndex", "Element index in the rung chain", true),
                SchemaBuilder.String("mnemonic", "New mnemonic (optional)"),
                SchemaBuilder.StringArray("operands", "Replacement operands")),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    int nodeIndex = RequireInt(args, "nodeIndex");
                    string? mnemonic = SchemaBuilder.Str(args, "mnemonic");
                    IReadOnlyList<string> operands = SchemaBuilder.StrArray(args, "operands");
                    ctx.Engine.Workspace.Mutate(p =>
                    {
                        Rung rung = RungAt(p, index);
                        if (nodeIndex < 0 || nodeIndex >= rung.Logic.Elements.Count || rung.Logic.Elements[nodeIndex] is not InstructionNode node)
                        {
                            throw new ArgumentOutOfRangeException("nodeIndex", "Element is not an instruction block.");
                        }

                        var call = new InstructionCall { Mnemonic = (mnemonic ?? node.Call.Mnemonic).Trim().ToUpperInvariant() };
                        foreach (string op in operands)
                        {
                            call.Operands.Add(Operand.Parse(op));
                        }

                        rung.Logic.Elements[nodeIndex] = new InstructionNode { Call = call };
                    }, "instruction_update");
                    return Task.FromResult(ToolResult.Text($"Instruction at rung {index + 1} element {nodeIndex} updated."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "instruction_delete",
            "Delete any element (contact/coil/instruction) from the rung's top-level chain by index.",
            SchemaBuilder.Object("Delete element",
                SchemaBuilder.Int("rung", "0-based index", true),
                SchemaBuilder.Int("nodeIndex", "Element index in the chain", true)),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    int index = RequireInt(args, "rung");
                    int nodeIndex = RequireInt(args, "nodeIndex");
                    ctx.Engine.Workspace.Mutate(p =>
                    {
                        Rung rung = RungAt(p, index);
                        if (nodeIndex < 0 || nodeIndex >= rung.Logic.Elements.Count)
                        {
                            throw new ArgumentOutOfRangeException("nodeIndex", "Element index out of range.");
                        }

                        rung.Logic.Elements.RemoveAt(nodeIndex);
                    }, "instruction_delete");
                    return Task.FromResult(ToolResult.Text($"Element {nodeIndex} removed from rung {index + 1}."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        // ==================== validation ====================
        reg.Add(new McpTool(
            "device_validate",
            "Check whether one device address (e.g. X17) is valid + writable for a model. SAFE, pure.",
            SchemaBuilder.Object("Validate device",
                SchemaBuilder.String("device", "Address text", true),
                SchemaBuilder.String("model", "Model id (default: current project target)")),
            SafetyClass.Safe,
            (ctx, args) =>
            {
                try
                {
                    string text = SchemaBuilder.Str(args, "device", "")!;
                    PlcModelDefinition m = ResolveModel(ctx, SchemaBuilder.Str(args, "model"));
                    return Task.FromResult(ToolResult.Text(ValidateDeviceText(text, m)));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "address_validate",
            "Batch device address validation against a model (one result line per address).",
            SchemaBuilder.Object("Validate addresses",
                SchemaBuilder.StringArray("devices", "Address list", true),
                SchemaBuilder.String("model", "Model id (default: current project target)")),
            SafetyClass.Safe,
            (ctx, args) =>
            {
                try
                {
                    PlcModelDefinition m = ResolveModel(ctx, SchemaBuilder.Str(args, "model"));
                    IReadOnlyList<string> devices = SchemaBuilder.StrArray(args, "devices");
                    string text = string.Join("\n", devices.Select(d => $"{d}: {ValidateDeviceText(d, m)}"));
                    return Task.FromResult(ToolResult.Text(text));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "project_validate",
            "Run the full validator (same code path as the IDE's validation).",
            SchemaBuilder.Object("Validate project"),
            SafetyClass.Safe,
            (ctx, _) =>
            {
                try
                {
                    var report = ctx.Engine.Validate();
                    int errors = report.Diagnostics.Count(d => d.Severity == Core.Validation.DiagnosticSeverity.Error);
                    int warnings = report.Diagnostics.Count(d => d.Severity == Core.Validation.DiagnosticSeverity.Warning);
                    return Task.FromResult(ToolResult.Json(new JsonObject
                    {
                        ["hasErrors"] = report.HasErrors,
                        ["summary"] = $"{errors} error(s), {warnings} warning(s)",
                        ["text"] = report.ToText(),
                    }));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "plc_get_instructions",
            "Dump the instruction database (mnemonic, category, operand shapes, semantic status, encoding-verified flag).",
            SchemaBuilder.Object("List instructions", SchemaBuilder.String("category", "Filter by category substring (optional)")),
            SafetyClass.Safe,
            (ctx, args) =>
            {
                string? cat = SchemaBuilder.Str(args, "category");
                IEnumerable<Core.Instructions.InstructionDefinition> defs = ctx.Engine.Catalog.All;
                if (cat is not null)
                {
                    defs = defs.Where(d => d.Category.ToString().Contains(cat, StringComparison.OrdinalIgnoreCase));
                }

                var arr = new JsonArray(defs.Select(d => (JsonNode)new JsonObject
                {
                    ["mnemonic"] = d.Mnemonic,
                    ["category"] = d.Category.ToString(),
                    ["fnc"] = d.Fnc,
                    ["summary"] = d.Summary,
                    ["operands"] = new JsonArray(d.Operands.Select(o => (JsonNode)new JsonObject
                    {
                        ["name"] = o.Name,
                        ["constraint"] = o.Constraint.ToString(),
                        ["optional"] = o.Optional,
                    }).ToArray()),
                    ["supportedFamilies"] = d.SupportedFamilies is null
                        ? null
                        : new JsonArray(d.SupportedFamilies.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
                    ["semanticStatus"] = d.SemanticStatus.ToString(),
                    ["encodingVerified"] = d.EncodingVerified,
                    ["doc"] = d.DocumentationRef,
                }).ToArray());
                return Task.FromResult(ToolResult.Json(arr));
            }));

        // ==================== compile / transfer ====================
        reg.Add(new McpTool(
            "compile_project",
            "Validate + assemble to the Delta symbolic listing. binaryEmittable=false is HONEST: Delta's object-code encoding is unpublished; " +
            "the reason field explains exactly what is missing.",
            SchemaBuilder.Object("Compile project"),
            SafetyClass.Safe,
            (ctx, _) =>
            {
                try
                {
                    var result = ctx.Engine.Compile();
                    return Task.FromResult(ToolResult.Json(new JsonObject
                    {
                        ["report"] = result.Report.ToText(),
                        ["hasErrors"] = result.Report.HasErrors,
                        ["listing"] = result.Listing,
                        ["binaryEmittable"] = result.BinaryEmittable,
                        ["binaryBlockedReason"] = result.BinaryBlockedReason,
                    }));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "export_project",
            "Save the current project to a directory in the documented open JSON format.",
            SchemaBuilder.Object("Export project", SchemaBuilder.String("path", "Target directory", true)),
            SafetyClass.Safe,
            async (ctx, args) =>
            {
                try
                {
                    RequireProject(ctx);
                    string path = SchemaBuilder.Str(args, "path", "")!;
                    await ctx.Engine.Workspace.SaveAsync(path, ctx.CancellationToken);
                    return ToolResult.Text($"Project exported to {path} (project.json, program/main.json, symbols.json).");
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "import_project",
            "Import a DeltaStudio project directory. WPLSoft files are NOT_IMPLEMENTED (proprietary format) — the tool explains that instead of guessing.",
            SchemaBuilder.Object("Import project", SchemaBuilder.String("path", "Directory or file", true)),
            SafetyClass.Safe,
            async (ctx, args) =>
            {
                try
                {
                    string path = SchemaBuilder.Str(args, "path", "")!;
                    if (Directory.Exists(path) && ProjectFileIO.IsProjectDirectory(path))
                    {
                        await ctx.Engine.Workspace.OpenAsync(path, ctx.CancellationToken);
                        return ToolResult.Text($"Imported DeltaStudio project from {path}.");
                    }

                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    bool wplsoft = ext is ".wpi" or ".wmp" or ".prj";
                    return ToolResult.Text(wplsoft
                        ? "WPLSoft project compatibility is NOT_IMPLEMENTED: the format is proprietary and undocumented; DeltaStudio will not guess it. " +
                          "A verified parser can be contributed as an adapter — see docs/delta-dvp.md (WPLSoft section)."
                        : "Path is neither a DeltaStudio project directory nor a supported import type.", true);
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        // ==================== connection ====================
        reg.Add(new McpTool(
            "connect_plc",
            "Connect: transport='serial' (RS-232/485 via project or given settings) or transport='mock' (in-process emulator, clearly labelled, offline use only).",
            SchemaBuilder.Object("Connect PLC",
                SchemaBuilder.Enum("transport", "Transport kind", true, "serial", "mock"),
                SchemaBuilder.String("port", "Serial port name (default: project settings)"),
                SchemaBuilder.Int("station", "MODBUS station (default: project)"),
                SchemaBuilder.Enum("mode", "Framing (default: project)", false, "RTU", "ASCII")),
            SafetyClass.Safe,
            async (ctx, args) =>
            {
                try
                {
                    string transport = SchemaBuilder.Str(args, "transport", "serial")!;
                    PlcProject? p = ctx.Engine.Workspace.Current;
                    int station = SchemaBuilder.IntArg(args, "station", p?.Target.ModbusStation ?? 1)!.Value;
                    bool ascii = string.Equals(
                        SchemaBuilder.Str(args, "mode", p?.Target.Communication.Mode ?? "RTU"), "ASCII", StringComparison.OrdinalIgnoreCase);

                    if (transport == "mock")
                    {
                        MockDvpPlc plc = ctx.Engine.CreateMockPlc((byte)station);
                        await ctx.Engine.ConnectMockAsync(plc, ascii, (byte)station, ctx.CancellationToken);
                        return ToolResult.Text("Connected to MOCK DVP PLC (in-process emulator; NOT hardware).");
                    }

                    var opts = new Protocols.Transport.SerialTransportOptions
                    {
                        PortName = SchemaBuilder.Str(args, "port", p?.Target.Communication.PortName ?? "COM3")!,
                        BaudRate = p?.Target.Communication.BaudRate ?? 9600,
                        DataBits = p?.Target.Communication.DataBits ?? 8,
                        StopBits = (p?.Target.Communication.StopBits ?? 1) == 2
                            ? System.IO.Ports.StopBits.Two
                            : System.IO.Ports.StopBits.One,
                        Parity = (p?.Target.Communication.Parity ?? "Even").ToLowerInvariant() switch
                        {
                            "none" => System.IO.Ports.Parity.None,
                            "odd" => System.IO.Ports.Parity.Odd,
                            _ => System.IO.Ports.Parity.Even,
                        },
                        ReadTimeoutMs = p?.Target.Communication.TimeoutMs ?? 1000,
                    };
                    await ctx.Engine.ConnectAsync(new Protocols.Transport.SerialPortTransport(opts), (byte)station, ascii, ctx.CancellationToken);
                    return ToolResult.Text($"Connected via {opts.PortName} station {station} {(ascii ? "ASCII" : "RTU")}. " +
                        "CPU-side serial parameters (D1120/D1130 on DVP) must match — verify before live use.");
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "disconnect_plc",
            "Close the PLC connection (does not change CPU state).",
            SchemaBuilder.Object("Disconnect"),
            SafetyClass.Safe,
            async (ctx, _) =>
            {
                await ctx.Engine.DisconnectAsync();
                return ToolResult.Text("Disconnected.");
            }));

        reg.Add(new McpTool(
            "plc_identify",
            "Ask the connected CPU to identify itself (DVP: MODBUS FC07 flow control — community-documented framing; advisory until hardware-verified).",
            SchemaBuilder.Object("Identify PLC"),
            SafetyClass.Safe,
            async (ctx, _) =>
            {
                try
                {
                    PlcConnection c = RequireConnection(ctx);
                    PlcIdentification id = await c.Link.IdentifyAsync(ctx.CancellationToken);
                    bool? run = await c.Link.QueryRunStateAsync(ctx.CancellationToken);
                    return ToolResult.Json(new JsonObject
                    {
                        ["vendor"] = id.Vendor,
                        ["modelCode"] = id.ModelCode,
                        ["version"] = id.Version,
                        ["resolvedModelId"] = id.ResolvedModelId,
                        ["verifiedAgainstHardware"] = id.VerifiedAgainstHardware,
                        ["runState"] = run is null ? "unknown" : (run.Value ? "RUN" : "STOP"),
                        ["notes"] = id.Notes,
                        ["transport"] = c.TransportDescription,
                        ["isMock"] = c.IsMock,
                    });
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        // ==================== device I/O ====================
        reg.Add(new McpTool(
            "plc_read_device",
            "Read a contiguous block of same-kind devices from the connected PLC (read-only, safe).",
            SchemaBuilder.Object("Read device",
                SchemaBuilder.String("device", "Start address (e.g. M0)", true),
                SchemaBuilder.Int("count", "Number of devices (default 1, max 250)")),
            SafetyClass.Safe,
            async (ctx, args) =>
            {
                try
                {
                    PlcConnection c = RequireConnection(ctx);
                    DeviceAddress start = DeviceAddress.Parse(SchemaBuilder.Str(args, "device", "")!);
                    int count = Math.Clamp(SchemaBuilder.IntArg(args, "count", 1)!.Value, 1, 250);
                    IReadOnlyList<DeviceValue> values = await c.Link.Devices.ReadBlockAsync(start, count, ctx.CancellationToken);
                    return ToolResult.Json(new JsonObject
                    {
                        ["start"] = start.ToString(),
                        ["values"] = new JsonArray(values
                            .Select((v, i) => (JsonNode)new JsonObject
                            {
                                ["address"] = new DeviceAddress(start.Kind, start.Number + i).ToString(),
                                ["value"] = v.ToString(),
                            })
                            .ToArray()),
                        ["mappingStatus"] = DvpModbusAddressMap.VerificationStatus.ToString(),
                        ["mappingNote"] = DvpModbusAddressMap.VerificationNote,
                    });
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "plc_write_device",
            "Write ONE device on a connected CPU. DANGEROUS: dry_run defaults to true; real execution requires a one-shot confirmation_token from safety_prepare_write bound to the exact arguments.",
            SchemaBuilder.Object("Write device",
                SchemaBuilder.String("device", "Address (M0, Y0, D100...)", true),
                SchemaBuilder.String("value", "Bit devices: 1/0/true/false; D/T/C: integer", true),
                SchemaBuilder.Bool("dry_run", "True (default): report only, no PLC write", false),
                SchemaBuilder.String("confirmation_token", "Token from safety_prepare_write")),
            SafetyClass.Dangerous,
            async (ctx, args) =>
            {
                try
                {
                    PlcConnection c = RequireConnection(ctx);
                    string device = SchemaBuilder.Str(args, "device", "")!;
                    string value = SchemaBuilder.Str(args, "value", "")!;
                    string canonical = $"{device}={value}";
                    string digest = SafetyTokenService.Digest("plc_write_device", canonical);

                    if (SchemaBuilder.BoolArg(args, "dry_run", true))
                    {
                        return ToolResult.Text(
                            $"DRY RUN: would write {value} to {device} via {c.TransportDescription}. " +
                            $"To execute: safety_prepare_write(operation='plc_write_device', canonical_args='{canonical}') → get token → retry with dry_run=false. digest={digest}");
                    }

                    if (RequireToken(ctx, digest, SchemaBuilder.Str(args, "confirmation_token"), out string? err))
                    {
                        return ToolResult.Text(err!, true);
                    }

                    DeviceAddress addr = DeviceAddress.Parse(device);
                    DeviceValue v = addr.Kind is DeviceKind.D or DeviceKind.T or DeviceKind.C
                        ? DeviceValue.FromWord(ushort.Parse(value))
                        : DeviceValue.FromBit(value is "1" or "true" or "TRUE" or "on" or "On");
                    await c.Link.Devices.WriteAsync(addr, v, ctx.CancellationToken);
                    return ToolResult.Text($"Wrote {value} to {device}. Read back with plc_read_device to confirm acceptance.");
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "plc_read_register",
            "Read D data registers (word block) — read-only, safe.",
            SchemaBuilder.Object("Read registers",
                SchemaBuilder.String("start", "D address (D0...)", true),
                SchemaBuilder.Int("count", "Count (default 1, max 125)")),
            SafetyClass.Safe,
            async (ctx, args) =>
            {
                try
                {
                    PlcConnection c = RequireConnection(ctx);
                    DeviceAddress start = DeviceAddress.Parse(SchemaBuilder.Str(args, "start", "")!);
                    if (start.Kind != DeviceKind.D)
                    {
                        return ToolResult.Text("plc_read_register expects D registers (use plc_read_device for T/C).", true);
                    }

                    int count = Math.Clamp(SchemaBuilder.IntArg(args, "count", 1)!.Value, 1, 125);
                    IReadOnlyList<DeviceValue> values = await c.Link.Devices.ReadBlockAsync(start, count, ctx.CancellationToken);
                    return ToolResult.Json(new JsonObject
                    {
                        ["start"] = start.ToString(),
                        ["values"] = new JsonArray(values
                            .Select((v, i) => (JsonNode)new JsonObject
                            {
                                ["address"] = new DeviceAddress(DeviceKind.D, start.Number + i).ToString(),
                                ["word"] = (int)v.Word,
                                ["signed"] = v.SignedWord,
                            })
                            .ToArray()),
                    });
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "plc_write_register",
            "Write D data registers (block). DANGEROUS: requires dry_run=false + confirmation_token from safety_prepare_write.",
            SchemaBuilder.Object("Write registers",
                SchemaBuilder.String("start", "First D address", true),
                SchemaBuilder.StringArray("values", "Integer values to write", true),
                SchemaBuilder.Bool("dry_run", "Default true", false),
                SchemaBuilder.String("confirmation_token", "From safety_prepare_write")),
            SafetyClass.Dangerous,
            async (ctx, args) =>
            {
                try
                {
                    string start = SchemaBuilder.Str(args, "start", "")!;
                    IReadOnlyList<string> values = SchemaBuilder.StrArray(args, "values");
                    string canonical = $"{start}=[{string.Join(",", values)}]";
                    string digest = SafetyTokenService.Digest("plc_write_register", canonical);

                    if (SchemaBuilder.BoolArg(args, "dry_run", true))
                    {
                        return ToolResult.Text(
                            $"DRY RUN: would write {values.Count} register(s) from {start} (values {string.Join(",", values)}). " +
                            $"safety_prepare_write(operation='plc_write_register', canonical_args='{canonical}') → digest={digest}");
                    }

                    if (RequireToken(ctx, digest, SchemaBuilder.Str(args, "confirmation_token"), out string? err))
                    {
                        return ToolResult.Text(err!, true);
                    }

                    PlcConnection c = RequireConnection(ctx);
                    DeviceAddress s = DeviceAddress.Parse(start);
                    var list = values.Select(v => DeviceValue.FromWord(checked((ushort)int.Parse(v)))).ToList();
                    await c.Link.Devices.WriteBlockAsync(s, list, ctx.CancellationToken);
                    return ToolResult.Text($"Wrote {list.Count} register(s) from {start}.");
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "plc_monitor",
            "Poll a bounded device list on a period and return all samples (read-only).",
            SchemaBuilder.Object("Monitor",
                SchemaBuilder.StringArray("devices", "Addresses, e.g. [\"X0\",\"Y0\",\"D10\",\"T0\",\"C0\"]", true),
                SchemaBuilder.Int("cycles", "Poll cycles (default 1, max 60)"),
                SchemaBuilder.Int("periodMs", "Delay between cycles (default 200, min 100)")),
            SafetyClass.Safe,
            async (ctx, args) =>
            {
                try
                {
                    RequireConnection(ctx);
                    IReadOnlyList<string> devices = SchemaBuilder.StrArray(args, "devices");
                    var addrs = devices.Select(DeviceAddress.Parse).ToList();
                    int cycles = Math.Clamp(SchemaBuilder.IntArg(args, "cycles", 1)!.Value, 1, 60);
                    var period = TimeSpan.FromMilliseconds(Math.Clamp(SchemaBuilder.IntArg(args, "periodMs", 200)!.Value, 100, 60000));
                    PlcMonitorService? monitor = ctx.Engine.Monitor ?? throw new InvalidOperationException("Monitor unavailable.");
                    var batches = await monitor!.CaptureAsync(addrs, cycles, period, ctx.CancellationToken);
                    var arr = new JsonArray(batches
                        .Select(b => (JsonNode)new JsonObject
                        {
                            ["t"] = b[0].TimestampUtc.ToString("O"),
                            ["values"] = new JsonArray(b
                                .Select(s => (JsonNode)new JsonObject
                                {
                                    ["addr"] = s.Address.ToString(),
                                    ["value"] = s.Value.ToString(),
                                    ["error"] = s.Error,
                                })
                                .ToArray()),
                        })
                        .ToArray());
                    return ToolResult.Json(arr);
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "diagnostics_get",
            "Connection + workspace + safety + capability-status summary (everything read-only).",
            SchemaBuilder.Object("Diagnostics"),
            SafetyClass.Safe,
            (ctx, _) =>
            {
                var report = ctx.Engine.Workspace.Current is null ? null : SafeValidate(ctx);
                return Task.FromResult(ToolResult.Json(new JsonObject
                {
                    ["connection"] = ctx.Engine.Active is null
                        ? "offline"
                        : new JsonObject
                        {
                            ["transport"] = ctx.Engine.Active.TransportDescription,
                            ["isMock"] = ctx.Engine.Active.IsMock,
                            ["connected"] = ctx.Engine.Active.Link.IsConnected,
                        },
                    ["project"] = ctx.Engine.Workspace.Current?.Metadata.Name ?? "(none)",
                    ["dirty"] = ctx.Engine.Workspace.IsDirty,
                    ["model"] = ctx.Engine.Workspace.Current?.Target.ModelId,
                    ["validationSummary"] = report,
                    ["outstandingConfirmations"] = ctx.Engine.Safety.OutstandingCount,
                    ["capabilityStatus"] = new JsonObject
                    {
                        ["projectSystem"] = "Implemented",
                        ["ladderSemanticModel"] = "Implemented",
                        ["validation"] = "Implemented",
                        ["compileListing"] = "Implemented (symbolic; binary intentionally gated)",
                        ["binaryEncoding"] = "NotImplemented — Delta object-code not published",
                        ["modbusRuntime"] = "PartiallySupported — mapping from docs, unverified on hardware",
                        ["programmingProtocol"] = "NotImplemented — WPLSoft protocol proprietary",
                        ["wplsoftImport"] = "NotImplemented — proprietary format",
                    },
                }));
            }));

        reg.Add(new McpTool(
            "plc_program_download",
            "Download object code to the CPU. NOT_IMPLEMENTED by policy (Delta programming protocol + encoding are unpublished). " +
            "Returns the exact enabling path; no silent transfer is possible.",
            SchemaBuilder.Object("Download",
                SchemaBuilder.Bool("dry_run", "Ignored — not implemented", false),
                SchemaBuilder.String("confirmation_token", "Ignored — not implemented")),
            SafetyClass.Critical,
            (ctx, _) =>
            {
                var r = ctx.Engine.Compile();
                return Task.FromResult(ToolResult.Json(new JsonObject
                {
                    ["status"] = "NOT_IMPLEMENTED",
                    ["reason"] = r.BinaryBlockedReason + " Additionally, the transfer framing (WPLSoft programming protocol) is proprietary.",
                    ["pathToEnable"] = new JsonArray
                    {
                        "1. Verify Delta object-code encoding (hardware capture or authoritative published material).",
                        "2. Implement IDvpObjectCodeEncoder and inject it into DvpAssembler.",
                        "3. Implement IDeltaProgrammingProtocol against the verified framing.",
                        "4. Add hardware integration tests before wiring this tool to the implementation.",
                    },
                    ["whatExistsInstead"] = new JsonArray
                    {
                        "compile_project — reviewed symbolic listing",
                        "export_project — open-format files for manual transfer into WPLSoft by a human",
                        "plc_write_* with confirmation — runtime parameter changes only",
                    },
                }));
            }));

        reg.Add(new McpTool(
            "plc_run_stop_control",
            "Put the connected CPU in RUN or STOP. NOT_IMPLEMENTED: requires the unpublished programming protocol. Read-only state via plc_run_state.",
            SchemaBuilder.Object("Run/stop control",
                SchemaBuilder.Enum("state", "Requested state", true, "run", "stop"),
                SchemaBuilder.String("confirmation_token", "Would be required once implemented")),
            SafetyClass.Critical,
            (_, _) => Task.FromResult(ToolResult.Json(new JsonObject
            {
                ["status"] = "NOT_IMPLEMENTED",
                ["reason"] = "DVP RUN/STOP switching is part of the proprietary programming-port protocol; DeltaStudio refuses to guess. " +
                             "Use the physical toggle or WPLSoft for now.",
            }))));

        reg.Add(new McpTool(
            "plc_run_state",
            "Read the DVP RUN supervision relay M1000 through MODBUS (read-only).",
            SchemaBuilder.Object("Run state"),
            SafetyClass.Safe,
            async (ctx, _) =>
            {
                try
                {
                    PlcConnection c = RequireConnection(ctx);
                    bool? run = await c.Link.QueryRunStateAsync(ctx.CancellationToken);
                    string text = run switch
                    {
                        null => "RUN state unknown (read failed, offline, or mapping unverified).",
                        true => "RUN",
                        false => "STOP",
                    };
                    return ToolResult.Text(text);
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        // ==================== safety plumbing ====================
        reg.Add(new McpTool(
            "safety_prepare_write",
            "Step 1 of a confirmed write. Returns the operation plan and a one-shot confirmation_token (2-min lifetime) " +
            "bound to the exact canonical args. Present the plan to a human before proceeding.",
            SchemaBuilder.Object("Prepare write",
                SchemaBuilder.Enum("operation", "Operation to confirm", true, "plc_write_device", "plc_write_register"),
                SchemaBuilder.String("canonical_args", "Exactly what the write will use: 'M0=1' or 'D100=[1,2,3]'", true)),
            SafetyClass.Safe,
            (ctx, args) =>
            {
                string operation = SchemaBuilder.Str(args, "operation", "")!;
                string canonical = SchemaBuilder.Str(args, "canonical_args", "")!;
                string digest = SafetyTokenService.Digest(operation, canonical);
                string token = ctx.Engine.Safety.Issue(digest, SafetyClass.Dangerous, operation + ":" + canonical);
                return Task.FromResult(ToolResult.Json(new JsonObject
                {
                    ["warning"] = "LIVE PLC WRITE PENDING — this changes machine behaviour. Human approval required.",
                    ["operation"] = operation,
                    ["canonicalArgs"] = canonical,
                    ["digest"] = digest,
                    ["confirmationToken"] = token,
                    ["expiresInSeconds"] = (int)ctx.Engine.Safety.Lifetime.TotalSeconds,
                    ["nextStep"] = $"Rerun {operation} with dry_run=false and confirmation_token='{token}'. Any argument change invalidates it.",
                }));
            }));

        reg.Add(new McpTool(
            "project_lock_acquire",
            "Advisory per-project lock for multi-agent safety (lease-based, renew by re-acquiring).",
            SchemaBuilder.Object("Lock acquire", SchemaBuilder.Int("leaseSeconds", "Lease duration (default 300)")),
            SafetyClass.ProjectWrite,
            async (ctx, args) =>
            {
                try
                {
                    string dir = RequireWorkspaceDir(ctx);
                    ProjectLockInfo res = await ctx.Engine.Registry.AcquireLockAsync(
                        dir, ctx.AgentId, SchemaBuilder.IntArg(args, "leaseSeconds", 300)!.Value, ctx.CancellationToken);
                    return ToolResult.Json(new JsonObject
                    {
                        ["acquired"] = res.Acquired,
                        ["holder"] = res.AgentId,
                        ["acquiredUtc"] = res.AcquiredUtc.ToString("O"),
                        ["leaseSeconds"] = res.LeaseSeconds,
                        ["note"] = res.Acquired
                            ? "Lock acquired. Release with project_lock_release when done."
                            : "Lock held by another agent (lease not expired). Do not edit; read-only is fine.",
                    });
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "project_lock_release",
            "Release the advisory project lock (holder only).",
            SchemaBuilder.Object("Lock release"),
            SafetyClass.ProjectWrite,
            async (ctx, _) =>
            {
                try
                {
                    string dir = RequireWorkspaceDir(ctx);
                    bool ok = await ctx.Engine.Registry.ReleaseLockAsync(dir, ctx.AgentId, ctx.CancellationToken);
                    return ToolResult.Text(ok ? "Lock released." : "No lock held by this agent for that path.", !ok);
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "project_lock_status",
            "Who holds the project lock (expired leases report as free).",
            SchemaBuilder.Object("Lock status"),
            SafetyClass.Safe,
            async (ctx, _) =>
            {
                try
                {
                    string dir = RequireWorkspaceDir(ctx);
                    ProjectLockInfo? info = await ctx.Engine.Registry.GetLockAsync(dir, ctx.CancellationToken);
                    return ToolResult.Json(info is null
                        ? (JsonNode)new JsonObject { ["locked"] = false }
                        : new JsonObject
                        {
                            ["locked"] = true,
                            ["holder"] = info.AgentId,
                            ["acquiredUtc"] = info.AcquiredUtc.ToString("O"),
                            ["leaseSeconds"] = info.LeaseSeconds,
                        });
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "project_checkpoints",
            "List automatic save checkpoints (latest 10) for rollback awareness.",
            SchemaBuilder.Object("Checkpoints"),
            SafetyClass.Safe,
            async (ctx, _) =>
            {
                try
                {
                    string dir = RequireWorkspaceDir(ctx);
                    var list = await ctx.Engine.Registry.ListCheckpointsAsync(dir, 10, ctx.CancellationToken);
                    return ToolResult.Json(new JsonArray(list
                        .Select(x => (JsonNode)new JsonObject
                        {
                            ["id"] = x.Id,
                            ["createdUtc"] = x.CreatedUtc.ToString("O"),
                            ["author"] = x.Author,
                            ["reason"] = x.Reason,
                        })
                        .ToArray()));
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "project_checkpoint_restore",
            "Restore the workspace from a checkpoint snapshot (in-memory; review then project_save).",
            SchemaBuilder.Object("Restore checkpoint", SchemaBuilder.Int("id", "Checkpoint id", true)),
            SafetyClass.ProjectWrite,
            async (ctx, args) =>
            {
                try
                {
                    int id = RequireInt(args, "id");
                    string? json = await ctx.Engine.Registry.GetCheckpointJsonAsync(id, ctx.CancellationToken);
                    if (json is null)
                    {
                        return ToolResult.Text($"Checkpoint {id} not found.", true);
                    }

                    PlcProject restored = JsonProjectSerializer.Deserialize(json);
                    ctx.Engine.Workspace.Mutate(p =>
                    {
                        ReplaceProgram(p, restored.Program);
                        p.Symbols = restored.Symbols;
                    }, "checkpoint_restore");
                    return ToolResult.Text($"Checkpoint {id} restored into the workspace (not saved yet — review then project_save).");
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        reg.Add(new McpTool(
            "project_recents",
            "Recent projects from the local registry.",
            SchemaBuilder.Object("Recents"),
            SafetyClass.Safe,
            async (ctx, _) =>
            {
                try
                {
                    var list = await ctx.Engine.Registry.ListRecentAsync(10, ctx.CancellationToken);
                    return ToolResult.Json(new JsonArray(list
                        .Select(x => (JsonNode)new JsonObject
                        {
                            ["path"] = x.Directory,
                            ["name"] = x.Name,
                            ["model"] = x.ModelId,
                            ["lastOpenedUtc"] = x.LastOpenedUtc.ToString("O"),
                            ["lastAgent"] = x.LastAgent,
                        })
                        .ToArray()));
                }
                catch (Exception e)
                {
                    return ToolResult.Text(e.Message, true);
                }
            }));

        // ==================== symbols ====================
        reg.Add(new McpTool(
            "symbol_upsert",
            "Add or update a named symbol bound to a device address.",
            SchemaBuilder.Object("Symbol upsert",
                SchemaBuilder.String("name", "Symbol name", true),
                SchemaBuilder.String("device", "Device address", true),
                SchemaBuilder.String("comment", "Comment")),
            SafetyClass.ProjectWrite,
            (ctx, args) =>
            {
                try
                {
                    string name = SchemaBuilder.Str(args, "name", "")!;
                    DeviceAddress addr = DeviceAddress.Parse(SchemaBuilder.Str(args, "device", "")!);
                    string? comment = SchemaBuilder.Str(args, "comment");
                    ctx.Engine.Workspace.Mutate(p =>
                    {
                        SymbolDefinition? existing = p.Symbols.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (existing is not null)
                        {
                            p.Symbols.Remove(existing);
                        }

                        p.Symbols.Add(new SymbolDefinition { Name = name, Address = addr, Comment = comment });
                    }, "symbol_upsert");
                    return Task.FromResult(ToolResult.Text($"Symbol {name} -> {addr} registered."));
                }
                catch (Exception e)
                {
                    return Task.FromResult(ToolResult.Text(e.Message, true));
                }
            }));

        reg.Add(new McpTool(
            "symbols_get",
            "List all symbols.",
            SchemaBuilder.Object("Symbols get"),
            SafetyClass.Safe,
            (ctx, _) =>
            {
                PlcProject p = RequireProject(ctx);
                return Task.FromResult(ToolResult.Json(new JsonArray(p.Symbols
                    .Select(s => (JsonNode)new JsonObject
                    {
                        ["name"] = s.Name,
                        ["device"] = s.Address.ToString(),
                        ["comment"] = s.Comment,
                    })
                    .ToArray())));
            }));

        return reg;
    }

    // ===================== shared plumbing =====================

    private static ContactPolarity ParsePolarity(string? p) => p?.ToLowerInvariant() switch
    {
        "nc" => ContactPolarity.NormallyClosed,
        "rising" => ContactPolarity.RisingEdge,
        "falling" => ContactPolarity.FallingEdge,
        _ => ContactPolarity.NormallyOpen,
    };

    private static Task<ToolResult> AddTimerCounter(McpToolContext ctx, JsonObject? args, string mnemonic, string devKey)
    {
        try
        {
            int index = RequireInt(args, "rung");
            string dev = SchemaBuilder.Str(args, devKey, "")!;
            string preset = SchemaBuilder.Str(args, "preset", "")!;
            string? contact = SchemaBuilder.Str(args, "contact");
            ctx.Engine.Workspace.Mutate(p =>
            {
                Rung rung = RungAt(p, index);
                if (contact is not null)
                {
                    rung.Logic.Add(new ContactNode
                    {
                        Device = DeviceAddress.Parse(contact),
                        Polarity = SchemaBuilder.Str(args, "contactPolarity") == "nc"
                            ? ContactPolarity.NormallyClosed
                            : ContactPolarity.NormallyOpen,
                    });
                }

                var call = new InstructionCall { Mnemonic = mnemonic };
                call.Operands.Add(new DeviceOperand(DeviceAddress.Parse(dev)));
                call.Operands.Add(Operand.Parse(preset));
                rung.Logic.Add(new InstructionNode { Call = call });
            }, mnemonic.ToLowerInvariant() + "_add");
            return Task.FromResult(ToolResult.Text($"{mnemonic} {dev} {preset} added to rung {index + 1}."));
        }
        catch (Exception e)
        {
            return Task.FromResult(ToolResult.Text(e.Message, true));
        }
    }

    /// <summary>OR-branch helper matching the editor semantics: wrap last element or extend an existing parallel.</summary>
    internal static void AddParallelContact(Rung rung, ContactNode contact)
    {
        if (rung.Logic.Elements.Count == 0)
        {
            rung.Logic.Add(contact);
            return;
        }

        LadderNode last = rung.Logic.Elements[^1];
        if (last is ParallelNetwork p)
        {
            p.Branches.Add(new SeriesNetwork { Elements = { contact } });
            return;
        }

        if (last is ContactNode)
        {
            var par = new ParallelNetwork();
            par.Branches.Add(new SeriesNetwork { Elements = { last } });
            par.Branches.Add(new SeriesNetwork { Elements = { contact } });
            rung.Logic.Elements[^1] = par;
            return;
        }

        throw new InvalidOperationException($"Cannot create a parallel branch against a {last.GetType().Name}.");
    }

    /// <summary>Returns true when the write must be REFUSED; error explains why.</summary>
    internal static bool RequireToken(McpToolContext ctx, string digest, string? token, out string? error)
    {
        bool accepted = ctx.Engine.Safety.ValidateAndConsume(token, digest, SafetyClass.Dangerous, out error);
        if (!accepted && error is null)
        {
            error = Refusal;
        }

        return !accepted;
    }

    internal static string ValidateDeviceText(string text, PlcModelDefinition model)
    {
        if (!DeviceAddress.TryParse(text, out DeviceAddress addr, out string? parseError))
        {
            return "INVALID FORMAT — " + parseError;
        }

        if (!model.TryGetDevice(addr.Kind, out DeviceCapabilities? caps) || caps is null)
        {
            return $"{addr} NOT SUPPORTED by {model.Id} (no {addr.Kind} devices on this model).";
        }

        if (!caps.Contains(addr))
        {
            return $"{addr} OUT OF RANGE for {model.Id} (allowed: {string.Join(", ", caps.Ranges)}).";
        }

        string writeNote = caps.Access switch
        {
            DeviceAccess.ReadOnly => "read-only",
            DeviceAccess.ContactAndValue => "contact read-only, present value read/write",
            _ => "read/write",
        };
        string verifyNote = caps.Verification == CapabilityStatus.Implemented ? string.Empty : $" [range data: {caps.Verification}]";
        return $"{addr} VALID for {model.Id} ({writeNote}){verifyNote}.";
    }

    private static string CapabilitiesText(PlcModelDefinition m)
    {
        var lines = new List<string>
        {
            m.Id + " — " + m.DisplayName,
            "family=" + m.Family + " programCapacity=" + m.ProgramCapacitySteps + " steps outputs=" + m.OutputType,
            "comm=" + m.Comm + " pulseAxes=" + m.PulseOutputAxes + " hsc=" + m.HighSpeedCounterChannels,
            "verification=" + m.Verification + (m.Notes is null ? string.Empty : " — " + m.Notes),
            "devices:",
        };
        foreach (KeyValuePair<DeviceKind, DeviceCapabilities> kv in m.Devices.OrderBy(kv => kv.Key))
        {
            lines.Add("  " + kv.Key + ": [" + string.Join(" | ", kv.Value.Ranges) + "] access=" + kv.Value.Access + " verified=" + kv.Value.Verification);
        }

        lines.Add("source: " + m.Source);
        return string.Join("\n", lines);
    }

    private static int RequireInt(JsonObject? args, string name)
    {
        int? v = SchemaBuilder.IntArg(args, name);
        if (v is null)
        {
            throw new ArgumentException("Missing required integer argument '" + name + "'.");
        }

        return v.Value;
    }

    private static PlcProject RequireProject(McpToolContext ctx) =>
        ctx.Engine.Workspace.Current ?? throw new InvalidOperationException("No project open. Use project_create or project_open first.");

    private static PlcConnection RequireConnection(McpToolContext ctx) =>
        ctx.Engine.Active ?? throw new InvalidOperationException("No PLC connection. Use connect_plc first (transport='mock' for a hardware-free session).");

    private static string RequireWorkspaceDir(McpToolContext ctx) =>
        ctx.Engine.Workspace.Directory ?? throw new InvalidOperationException("Project has no directory yet; run project_save first.");

    private static PlcModelDefinition ResolveModel(McpToolContext ctx, string? modelId)
    {
        if (modelId is not null)
        {
            return ProjectWorkspace.ResolveModel(modelId);
        }

        PlcProject p = RequireProject(ctx);
        return ProjectWorkspace.ResolveModel(p.Target.ModelId);
    }

    private static Rung RungAt(PlcProject p, int index)
    {
        if (index < 0 || index >= p.Program.Main.Count)
        {
            throw new ArgumentOutOfRangeException("rung", "Rung " + index + " does not exist (project has " + p.Program.Main.Count + " rungs).");
        }

        return p.Program.Main[index];
    }

    private static void ReplaceProgram(PlcProject project, PlcProgram program)
    {
        project.Program.Main.Clear();
        foreach (Rung r in program.Main)
        {
            project.Program.Main.Add(r);
        }
    }

    private static string SafeValidate(McpToolContext ctx)
    {
        try
        {
            return ctx.Engine.Validate().ToText();
        }
        catch (Exception e)
        {
            return "validation skipped: " + e.Message;
        }
    }
}

/// <summary>Whole-project JSON (checkpoints; not the on-disk split format).</summary>
public static class JsonProjectSerializer
{
    /// <summary>Serializes project + program + symbols into one JSON document.</summary>
    public static string Serialize(PlcProject p)
    {
        var o = new JsonObject
        {
            ["project"] = JsonNode.Parse(PlcProjectJson.SerializeProject(p)),
            ["program"] = JsonNode.Parse(PlcProjectJson.SerializeProgram(p.Program)),
            ["symbols"] = JsonNode.Parse(PlcProjectJson.SerializeSymbols(p.Symbols)),
        };
        return o.ToJsonString();
    }

    /// <summary>Restores a project from the combined JSON.</summary>
    public static PlcProject Deserialize(string json)
    {
        JsonObject o = JsonNode.Parse(json)!.AsObject();
        var p = new PlcProject();
        PlcProjectJson.ApplyProjectJson(p, o["project"]!.ToJsonString());
        p.Program = PlcProjectJson.DeserializeProgram(o["program"]!.ToJsonString());
        p.Symbols = PlcProjectJson.DeserializeSymbols(o["symbols"]!.ToJsonString());
        return p;
    }
}
