using System.Text.Json.Nodes;
using DeltaStudio.Infrastructure;
using DeltaStudio.Infrastructure.Safety;

namespace DeltaStudio.Mcp.Tools;

/// <summary>Execution context handed to every tool.</summary>
public sealed class McpToolContext
{
    /// <summary>Shared engine (workspace, validator, plc links, safety).</summary>
    public required DeltaStudioEngine Engine { get; init; }

    /// <summary>Agent identity for lock attribution (from clientInfo or _meta.agentId; default "default-agent").</summary>
    public required string AgentId { get; init; }

    /// <summary>Request cancellation.</summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Content item in an MCP tool result.</summary>
public sealed record ToolContent(string Type, string Text)
{
    /// <summary>Renders as a JSON content block.</summary>
    public JsonObject ToJson() => new() { ["type"] = Type, ["text"] = Text };
}

/// <summary>Tool invocation outcome.</summary>
public sealed class ToolResult
{
    /// <summary>Content blocks.</summary>
    public List<ToolContent> Content { get; } = new();

    /// <summary>Error flag.</summary>
    public bool IsError { get; set; }

    /// <summary>Text result.</summary>
    public static ToolResult Text(string text, bool error = false) =>
        new ToolResult { IsError = error }.With(text);

    /// <summary>JSON result (pretty).</summary>
    public static ToolResult Json(JsonNode? node, bool error = false) =>
        new ToolResult { IsError = error }.With(node?.ToJsonString(PrettyOptions) ?? "null");

    /// <summary>Indented writer that copies the default resolver (required for JsonNode serialization).</summary>
    public static readonly System.Text.Json.JsonSerializerOptions PrettyOptions =
        new(System.Text.Json.JsonSerializerOptions.Default) { WriteIndented = true };

    /// <summary>Adds text content.</summary>
    public ToolResult With(string text)
    {
        Content.Add(new ToolContent("text", text));
        return this;
    }

    /// <summary>Renders the MCP result object.</summary>
    public JsonObject ToJson()
    {
        var arr = new JsonArray();
        foreach (ToolContent c in Content)
        {
            arr.Add(c.ToJson());
        }

        return new JsonObject { ["content"] = arr, ["isError"] = IsError };
    }
}

/// <summary>One MCP tool definition.</summary>
public sealed record McpTool(
    string Name,
    string Description,
    JsonObject InputSchema,
    SafetyClass Safety,
    Func<McpToolContext, JsonObject?, Task<ToolResult>> Handler);

/// <summary>Tool collection + name lookup (server-agnostic so the registry is unit-testable in-process).</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, McpTool> _tools = new(StringComparer.Ordinal);

    /// <summary>Registers a tool; throws on duplicates.</summary>
    public void Add(McpTool tool)
    {
        if (!_tools.TryAdd(tool.Name, tool))
        {
            throw new InvalidOperationException($"Duplicate MCP tool '{tool.Name}'.");
        }
    }

    /// <summary>Looks up a tool.</summary>
    public bool TryGet(string name, out McpTool tool) => _tools.TryGetValue(name, out tool!);

    /// <summary>All tools.</summary>
    public IReadOnlyCollection<McpTool> All => _tools.Values;

    /// <summary>Renders the MCP tools/list result payload.</summary>
    public JsonArray ToListPayload()
    {
        var arr = new JsonArray();
        foreach (McpTool t in _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var annotations = new JsonObject
            {
                ["readOnlyHint"] = t.Safety == SafetyClass.Safe,
                ["destructiveHint"] = t.Safety is SafetyClass.Dangerous or SafetyClass.Critical,
                ["idempotentHint"] = false,
                ["openWorldHint"] = t.Safety is SafetyClass.Dangerous or SafetyClass.Critical,
            };
            arr.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema,
                ["annotations"] = annotations,
                ["_deltastudio_safety_class"] = t.Safety.ToString(),
            });
        }

        return arr;
    }
}
