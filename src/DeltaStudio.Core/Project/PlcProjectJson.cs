using System.Text.Json;
using System.Text.Json.Serialization;
using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Program;

namespace DeltaStudio.Core.Project;

/*
 * Explicit DTO layer so the wire format is stable, documented and versioned, independent of
 * C# type layout. See docs/project-format.md. Nodes are discriminated by "type".
 */

internal sealed class NodeDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("device")] public string? Device { get; set; }
    [JsonPropertyName("polarity")] public string? Polarity { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("elements")] public List<NodeDto>? Elements { get; set; }
    [JsonPropertyName("branches")] public List<NodeDto>? Branches { get; set; }
    [JsonPropertyName("mnemonic")] public string? Mnemonic { get; set; }
    [JsonPropertyName("operands")] public List<string>? Operands { get; set; }
}

internal sealed class RungDto
{
    [JsonPropertyName("number")] public int Number { get; set; }
    [JsonPropertyName("comment")] public string? Comment { get; set; }
    [JsonPropertyName("layoutRow")] public int? LayoutRow { get; set; }
    [JsonPropertyName("logic")] public required NodeDto Logic { get; set; }
}

internal sealed class ProgramDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = PlcProjectJson.SchemaVersion;
    [JsonPropertyName("rungs")] public List<RungDto> Rungs { get; set; } = new();
}

internal sealed class SymbolDto
{
    [JsonPropertyName("name")] public required string Name { get; set; }
    [JsonPropertyName("address")] public required string Address { get; set; }
    [JsonPropertyName("comment")] public string? Comment { get; set; }
}

internal sealed class SymbolsDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = PlcProjectJson.SchemaVersion;
    [JsonPropertyName("symbols")] public List<SymbolDto> Symbols { get; set; } = new();
}

internal sealed class TargetDto
{
    [JsonPropertyName("modelId")] public string ModelId { get; set; } = "DVP14SS2T";
    [JsonPropertyName("modbusStation")] public int ModbusStation { get; set; } = 1;
    [JsonPropertyName("communication")] public CommDto Communication { get; set; } = new();
}

internal sealed class CommDto
{
    [JsonPropertyName("portName")] public string PortName { get; set; } = "COM3";
    [JsonPropertyName("baudRate")] public int BaudRate { get; set; } = 9600;
    [JsonPropertyName("parity")] public string Parity { get; set; } = "Even";
    [JsonPropertyName("stopBits")] public int StopBits { get; set; } = 1;
    [JsonPropertyName("dataBits")] public int DataBits { get; set; } = 8;
    [JsonPropertyName("mode")] public string Mode { get; set; } = "RTU";
    [JsonPropertyName("timeoutMs")] public int TimeoutMs { get; set; } = 1000;
}

internal sealed class ProjectFileDto
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = PlcProjectJson.SchemaVersion;
    [JsonPropertyName("kind")] public string Kind { get; set; } = "DeltaStudio.Project";
    [JsonPropertyName("metadata")] public MetadataDto Metadata { get; set; } = new();
    [JsonPropertyName("target")] public TargetDto Target { get; set; } = new();
    [JsonPropertyName("options")] public Dictionary<string, string> Options { get; set; } = new();
}

internal sealed class MetadataDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "Untitled";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("createdUtc")] public DateTimeOffset CreatedUtc { get; set; }
    [JsonPropertyName("modifiedUtc")] public DateTimeOffset ModifiedUtc { get; set; }
    [JsonPropertyName("tool")] public string Tool { get; set; } = "DeltaStudio";
    [JsonPropertyName("toolVersion")] public string ToolVersion { get; set; } = "0.1.0";
}

/// <summary>JSON (de)serialization for the ladder IR and the project model.</summary>
public static class PlcProjectJson
{
    /// <summary>Current schema version of the JSON project format.</summary>
    public const int SchemaVersion = 1;

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Serializes a rung list to the program/main.json payload.</summary>
    public static string SerializeProgram(PlcProgram program)
    {
        var dto = new ProgramDto();
        for (int i = 0; i < program.Main.Count; i++)
        {
            Rung r = program.Main[i];
            dto.Rungs.Add(new RungDto
            {
                Number = i + 1,
                Comment = r.Comment,
                LayoutRow = r.LayoutRow,
                Logic = ToDto(r.Logic),
            });
        }

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Parses a program/main.json payload.</summary>
    public static PlcProgram DeserializeProgram(string json)
    {
        ProgramDto? dto = JsonSerializer.Deserialize<ProgramDto>(json, Options)
            ?? throw new FormatException("program/main.json is empty or malformed.");
        if (dto.SchemaVersion != SchemaVersion)
        {
            throw new NotSupportedException($"Program schema version {dto.SchemaVersion} is not supported (expected {SchemaVersion}).");
        }

        var program = new PlcProgram();
        foreach (RungDto r in dto.Rungs)
        {
            program.Main.Add(new Rung
            {
                Comment = r.Comment,
                LayoutRow = r.LayoutRow,
                Logic = (SeriesNetwork)FromDto(r.Logic),
            });
        }

        return program;
    }

    /// <summary>Serializes symbols to symbols.json payload.</summary>
    public static string SerializeSymbols(IReadOnlyList<SymbolDefinition> symbols)
    {
        var dto = new SymbolsDto();
        foreach (SymbolDefinition s in symbols)
        {
            dto.Symbols.Add(new SymbolDto { Name = s.Name, Address = s.Address.ToString(), Comment = s.Comment });
        }

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Parses symbols.json payload.</summary>
    public static List<SymbolDefinition> DeserializeSymbols(string json)
    {
        SymbolsDto? dto = JsonSerializer.Deserialize<SymbolsDto>(json, Options) ?? new SymbolsDto();
        var list = new List<SymbolDefinition>();
        foreach (SymbolDto s in dto.Symbols)
        {
            list.Add(new SymbolDefinition
            {
                Name = s.Name,
                Address = DeviceAddress.Parse(s.Address),
                Comment = s.Comment,
            });
        }

        return list;
    }

    /// <summary>Serializes project.json payload (metadata + target + options).</summary>
    public static string SerializeProject(PlcProject project)
    {
        var dto = new ProjectFileDto
        {
            Metadata = new MetadataDto
            {
                Name = project.Metadata.Name,
                Description = project.Metadata.Description,
                Author = project.Metadata.Author,
                CreatedUtc = project.Metadata.CreatedUtc,
                ModifiedUtc = project.Metadata.ModifiedUtc,
                Tool = project.Metadata.Tool,
                ToolVersion = project.Metadata.ToolVersion,
            },
            Target = new TargetDto
            {
                ModelId = project.Target.ModelId,
                ModbusStation = project.Target.ModbusStation,
                Communication = new CommDto
                {
                    PortName = project.Target.Communication.PortName,
                    BaudRate = project.Target.Communication.BaudRate,
                    Parity = project.Target.Communication.Parity,
                    StopBits = project.Target.Communication.StopBits,
                    DataBits = project.Target.Communication.DataBits,
                    Mode = project.Target.Communication.Mode,
                    TimeoutMs = project.Target.Communication.TimeoutMs,
                },
            },
            Options = new Dictionary<string, string>(project.Options, StringComparer.OrdinalIgnoreCase),
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Applies a parsed project.json payload onto an existing (or fresh) project.</summary>
    public static void ApplyProjectJson(PlcProject project, string json)
    {
        ProjectFileDto? dto = JsonSerializer.Deserialize<ProjectFileDto>(json, Options)
            ?? throw new FormatException("project.json is empty or malformed.");
        if (dto.SchemaVersion != SchemaVersion)
        {
            throw new NotSupportedException($"Project schema version {dto.SchemaVersion} is not supported (expected {SchemaVersion}).");
        }

        if (!string.Equals(dto.Kind, "DeltaStudio.Project", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Not a DeltaStudio project file (kind = '{dto.Kind}').");
        }

        project.Metadata = new PlcProjectMetadata
        {
            Name = dto.Metadata.Name,
            Description = dto.Metadata.Description,
            Author = dto.Metadata.Author,
            CreatedUtc = dto.Metadata.CreatedUtc,
            ModifiedUtc = dto.Metadata.ModifiedUtc,
            Tool = dto.Metadata.Tool,
            ToolVersion = dto.Metadata.ToolVersion,
        };
        project.Target = new PlcTarget
        {
            ModelId = dto.Target.ModelId,
            ModbusStation = dto.Target.ModbusStation,
            Communication = new CommSettings
            {
                PortName = dto.Target.Communication.PortName,
                BaudRate = dto.Target.Communication.BaudRate,
                Parity = dto.Target.Communication.Parity,
                StopBits = dto.Target.Communication.StopBits,
                DataBits = dto.Target.Communication.DataBits,
                Mode = dto.Target.Communication.Mode,
                TimeoutMs = dto.Target.Communication.TimeoutMs,
            },
        };
        project.Options = new Dictionary<string, string>(dto.Options, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Serializes a single rung's logic (used by MCP rung_get).</summary>
    public static string SerializeNode(LadderNode node) => JsonSerializer.Serialize(ToDto(node), Options);

    /// <summary>Parses a single node JSON (used by MCP ladder_add_* element construction).</summary>
    public static LadderNode DeserializeNode(string json)
        => FromDto(JsonSerializer.Deserialize<NodeDto>(json, Options)
            ?? throw new FormatException("Node JSON is empty."));

    internal static NodeDto ToDto(LadderNode node) => node switch
    {
        SeriesNetwork s => new NodeDto
        {
            Type = "series",
            Elements = s.Elements.ConvertAll(ToDto),
        },
        ParallelNetwork p => new NodeDto
        {
            Type = "parallel",
            Branches = p.Branches.ConvertAll(b => ToDto(b)),
        },
        ContactNode c => new NodeDto
        {
            Type = "contact",
            Device = c.Device.ToString(),
            Polarity = c.Polarity switch
        {
            ContactPolarity.NormallyOpen => "no",
            ContactPolarity.NormallyClosed => "nc",
            ContactPolarity.RisingEdge => "rising",
            _ => "falling",
        },
            Label = c.Label,
        },
        CoilNode k => new NodeDto
        {
            Type = "coil",
            Device = k.Device.ToString(),
            Action = k.Action switch
            {
                CoilAction.Output => "out",
                CoilAction.Set => "set",
                _ => "rst",
            },
            Label = k.Label,
        },
        InstructionNode i => new NodeDto
        {
            Type = "call",
            Mnemonic = i.Call.Mnemonic,
            Operands = i.Call.Operands.ConvertAll(o => o.ToText()),
        },
        _ => throw new NotSupportedException($"Unknown node type {node.GetType().Name}."),
    };

    internal static LadderNode FromDto(NodeDto dto)
    {
        switch (dto.Type)
        {
            case "series":
                var s = new SeriesNetwork();
                foreach (NodeDto e in dto.Elements ?? new List<NodeDto>())
                {
                    s.Add(FromDto(e));
                }

                return s;
            case "parallel":
                var p = new ParallelNetwork();
                foreach (NodeDto b in dto.Branches ?? new List<NodeDto>())
                {
                    if (FromDto(b) is not SeriesNetwork sn)
                    {
                        throw new FormatException("Every parallel branch must be a series network.");
                    }

                    p.Add(sn);
                }

                return p;
            case "contact":
                return new ContactNode
                {
                    Device = DeviceAddress.Parse(dto.Device ?? throw new FormatException("contact requires 'device'.")),
                    Polarity = dto.Polarity switch
                    {
                        null or "no" => ContactPolarity.NormallyOpen,
                        "nc" => ContactPolarity.NormallyClosed,
                        "rising" => ContactPolarity.RisingEdge,
                        "falling" => ContactPolarity.FallingEdge,
                        _ => throw new FormatException($"Unknown polarity '{dto.Polarity}'."),
                    },
                    Label = dto.Label,
                };
            case "coil":
                return new CoilNode
                {
                    Device = DeviceAddress.Parse(dto.Device ?? throw new FormatException("coil requires 'device'.")),
                    Action = dto.Action switch
                    {
                        null or "out" => CoilAction.Output,
                        "set" => CoilAction.Set,
                        "rst" => CoilAction.Reset,
                        _ => throw new FormatException($"Unknown coil action '{dto.Action}'."),
                    },
                    Label = dto.Label,
                };
            case "call":
                var call = new InstructionCall { Mnemonic = dto.Mnemonic ?? throw new FormatException("call requires 'mnemonic'.") };
                foreach (string op in dto.Operands ?? new List<string>())
                {
                    call.Operands.Add(Operand.Parse(op));
                }

                return new InstructionNode { Call = call };
            default:
                throw new FormatException($"Unknown node type '{dto.Type}'.");
        }
    }
}
