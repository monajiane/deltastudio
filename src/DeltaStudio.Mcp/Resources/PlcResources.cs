using System.Text.Json.Nodes;
using System.Text.Json;
using DeltaStudio.Core.Project;
using DeltaStudio.Mcp.Tools;
using DeltaStudio.Compiler.Il;
using DeltaStudio.Delta.Comm;
using DeltaStudio.Infrastructure;
using DeltaStudio.Infrastructure.Safety;

namespace DeltaStudio.Mcp.Resources;

/// <summary>
/// MCP resources: read-only, token-free views into the live engine state, so an agent can
/// inspect the whole project with one resources/read instead of a tool-call storm.
/// </summary>
public static class PlcResources
{
    /// <summary>resources/list payload.</summary>
    public static JsonObject List() => new()
    {
        ["resources"] = new JsonArray
        {
            Res("plc://current/project", "Full project (metadata + target + program IR + symbols) as one JSON document.", "application/json"),
            Res("plc://current/program", "Main program rendered as Delta IL text (human/agent readable).", "text/plain"),
            Res("plc://current/program/ir", "Main program JSON IR exactly as stored in program/main.json.", "application/json"),
            Res("plc://current/diagnostics", "Last validation report + connection + safety state.", "application/json"),
            Res("plc://current/model", "Selected model definition JSON (id, family, capacities, verification).", "application/json"),
            Res("plc://current/capabilities", "Device capability table (ranges per device kind, verification flags).", "text/plain"),
            Res("plc://current/device-map", "MODBUS address map entries in use (PartiallySupported: unverified on hardware).", "application/json"),
            Res("plc://current/instructions", "Instruction catalog for the Delta backend (statuses included).", "application/json"),
        },
    };

    /// <summary>resources/templates/list payload.</summary>
    public static JsonObject ListTemplates() => new()
    {
        ["resourceTemplates"] = new JsonArray
        {
            new JsonObject
            {
                ["uriTemplate"] = "plc://current/rung/{index}",
                ["name"] = "rung",
                ["description"] = "One rung's JSON IR by 0-based index.",
                ["mimeType"] = "application/json",
            },
        },
    };

    /// <summary>resources/read implementation.</summary>
    public static async Task<JsonObject> ReadAsync(DeltaStudioEngine engine, JsonObject? args, string agentId)
    {
        string uri = args?["uri"]?.GetValue<string>() ?? throw new InvalidOperationException("resources/read requires 'uri'.");

        // uri-template resources (rung/{index})
        if (uri.StartsWith("plc://current/rung/", StringComparison.Ordinal))
        {
            string rest = uri["plc://current/rung/".Length..];
            if (!int.TryParse(rest, out int index) || engine.Workspace.Current is null
                || index < 0 || index >= engine.Workspace.Current.Program.Main.Count)
            {
                throw new ArgumentException($"Cannot resolve '{uri}' (need 0 <= index < rung count).");
            }

            return OneText(uri, PlcProjectJson.SerializeNode(engine.Workspace.Current.Program.Main[index].Logic), "application/json");
        }

        var project = engine.Workspace.Current;
        string text = uri switch
        {
            "plc://current/project" => project is null
                ? "{\"error\":\"no project open\"}"
                : JsonProjectSerializer.Serialize(project),
            "plc://current/program" => project is null
                ? "(no project open)"
                : new IlFormatter().Format(project.Program),
            "plc://current/program/ir" => project is null
                ? "(no project open)"
                : PlcProjectJson.SerializeProgram(project.Program),
            "plc://current/model" => project is null
                ? "(no project open)"
                : ModelJson(ProjectWorkspace.ResolveModel(project.Target.ModelId)).ToJsonString(Serialization.DefaultIndented),
            "plc://current/capabilities" => project is null
                ? "(no project open)"
                : CapabilitiesJson(ProjectWorkspace.ResolveModel(project.Target.ModelId)).ToJsonString(Serialization.DefaultIndented),
            "plc://current/device-map" => MapJson().ToJsonString(Serialization.DefaultIndented),
            "plc://current/diagnostics" => DiagnosticsJson(engine, agentId).ToJsonString(Serialization.DefaultIndented),
            "plc://current/instructions" => InstructionsJson(engine).ToJsonString(Serialization.DefaultIndented),
            _ => throw new ArgumentException($"Unknown resource uri '{uri}'. See resources/list."),
        };

        await Task.CompletedTask;
        return OneText(uri, text, uri is "plc://current/program" or "plc://current/capabilities" ? "text/plain" : "application/json");
    }

    private static JsonObject OneText(string uri, string text, string mime) => new()
    {
        ["contents"] = new JsonArray(new JsonObject
        {
            ["uri"] = uri,
            ["mimeType"] = mime,
            ["text"] = text,
        }),
    };

    private static JsonObject Res(string uri, string name, string mime) => new()
    {
        ["uri"] = uri,
        ["name"] = name,
        ["mimeType"] = mime,
    };

    private static JsonNode ModelJson(Core.Model.PlcModelDefinition m) => new JsonObject
    {
        ["id"] = m.Id,
        ["family"] = m.Family,
        ["displayName"] = m.DisplayName,
        ["programCapacitySteps"] = m.ProgramCapacitySteps,
        ["comm"] = m.Comm.ToString(),
        ["pulseOutputAxes"] = m.PulseOutputAxes,
        ["highSpeedCounterChannels"] = m.HighSpeedCounterChannels,
        ["outputType"] = m.OutputType,
        ["verification"] = m.Verification.ToString(),
        ["source"] = m.Source,
        ["notes"] = m.Notes,
    };

    private static JsonNode CapabilitiesJson(Core.Model.PlcModelDefinition m)
    {
        var o = new JsonObject();
        foreach ((Core.Devices.DeviceKind kind, Core.Devices.DeviceCapabilities caps) in m.Devices)
        {
            o[kind.ToString()] = new JsonObject
            {
                ["ranges"] = new JsonArray(caps.Ranges
                    .Select(r => (JsonNode)new JsonObject { ["min"] = r.Min, ["max"] = r.Max, ["label"] = r.Label })
                    .ToArray()),
                ["access"] = caps.Access.ToString(),
                ["verification"] = caps.Verification.ToString(),
                ["notes"] = caps.Notes,
            };
        }

        return new JsonObject
        {
            ["model"] = m.Id,
            ["octalDeviceKinds"] = new JsonArray("X", "Y"),
            ["devices"] = o,
            ["source"] = m.Source,
        };
    }

    private static JsonNode MapJson()
    {
        var map = new DvpModbusAddressMap();
        return new JsonObject
        {
            ["status"] = DvpModbusAddressMap.VerificationStatus.ToString(),
            ["note"] = DvpModbusAddressMap.VerificationNote,
            ["entries"] = new JsonArray(map.Entries
                .Select(e => (JsonNode)new JsonObject
                {
                    ["area"] = e.Area.ToString(),
                    ["modbusStart"] = e.ModbusStart,
                    ["modbusStartHex"] = "0x" + e.ModbusStart.ToString("X4"),
                    ["deviceKind"] = e.Kind.ToString(),
                    ["deviceStart"] = e.DeviceStart,
                    ["count"] = e.Count,
                    ["label"] = e.Label,
                })
                .ToArray()),
        };
    }

    private static JsonNode DiagnosticsJson(DeltaStudioEngine engine, string agentId)
    {
        var o = new JsonObject
        {
            ["agent"] = agentId,
            ["connection"] = engine.Active is null
                ? "offline"
                : new JsonObject
                {
                    ["transport"] = engine.Active.TransportDescription,
                    ["isMock"] = engine.Active.IsMock,
                    ["identified"] = engine.Active.Identification is null
                        ? null
                        : $"{engine.Active.Identification.Vendor} model=0x{engine.Active.Identification.ModelCode:X4} v0x{engine.Active.Identification.Version:X4} verified={engine.Active.Identification.VerifiedAgainstHardware}",
                },
            ["outstandingConfirmations"] = engine.Safety.OutstandingCount,
            ["project"] = engine.Workspace.Current?.Metadata.Name ?? "(none)",
            ["dirty"] = engine.Workspace.IsDirty,
        };

        if (engine.Workspace.Current is not null)
        {
            try
            {
                o["validation"] = engine.Validate().ToText();
            }
            catch (Exception e)
            {
                o["validation"] = "validation failed: " + e.Message;
            }
        }

        return o;
    }

    private static JsonNode InstructionsJson(DeltaStudioEngine engine) => new JsonArray(engine.Catalog.All
        .Select(d => (JsonNode)new JsonObject
        {
            ["mnemonic"] = d.Mnemonic,
            ["category"] = d.Category.ToString(),
            ["fnc"] = d.Fnc,
            ["summary"] = d.Summary,
            ["operands"] = new JsonArray(d.Operands
                .Select(o => (JsonNode)new JsonObject { ["name"] = o.Name, ["constraint"] = o.Constraint.ToString(), ["optional"] = o.Optional })
                .ToArray()),
            ["semanticStatus"] = d.SemanticStatus.ToString(),
            ["encodingVerified"] = d.EncodingVerified,
        })
        .ToArray());

}

/// <summary>Shared JSON options for the MCP layer.</summary>
internal static class Serialization
{
    /// <summary>Indented writer options (inherits the default resolver).</summary>
    public static readonly JsonSerializerOptions DefaultIndented =
        new(JsonSerializerOptions.Default) { WriteIndented = true };
}
