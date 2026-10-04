using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeltaStudio.Mcp.Prompts;
using DeltaStudio.Mcp.Protocol;
using DeltaStudio.Mcp.Resources;
using DeltaStudio.Mcp.Tools;
using DeltaStudio.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DeltaStudio.Mcp;

/// <summary>
/// MCP server (Model Context Protocol) over the stdio JSON-RPC 2.0 transport.
/// Framing: one JSON-RPC message per line (per MCP stdio spec). Supported protocol versions:
/// 2024-11-05 (also answers 2025-03-26/2025-06-18 as it implements no features removed/added between them
/// except where noted). Transports beyond stdio (streamable HTTP) are architecturally compatible —
/// the dispatcher (ProcessAsync) is transport-agnostic — but only stdio is implemented today.
/// </summary>
public sealed class McpServer
{
    private static readonly string[] SupportedProtocolVersions = ["2024-11-05", "2025-03-26", "2025-06-18"];
    private const string FallbackProtocolVersion = "2024-11-05";

    private readonly DeltaStudioEngine _engine;
    private readonly ToolRegistry _tools;
    private readonly ILogger<McpServer>? _logger;
    private string _agentId = "default-agent";
    private bool _initialized;

    /// <summary>Server name reported in initialize.</summary>
    public const string ServerName = "deltastudio";

    /// <summary>Server version reported in initialize.</summary>
    public const string ServerVersion = "0.1.0";

    /// <summary>Creates the server over an engine (the headless composition used by the CLI/tests).</summary>
    public McpServer(DeltaStudioEngine engine, ILogger<McpServer>? logger = null)
    {
        _engine = engine;
        _tools = PlcToolset.Build();
        _logger = logger;
    }

    /// <summary>Tool registry (test seam: call handlers directly without JSON framing).</summary>
    public ToolRegistry Tools => _tools;

    /// <summary>Runs the stdio loop: newline-delimited JSON-RPC on stdin/stdout (stderr for logs).</summary>
    public async Task RunStdioAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        var writeGate = new SemaphoreSlim(1, 1);

        async Task WriteAsync(JsonObject message)
        {
            await writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Console.OpenStandardOutput().WriteAsync(Encoding.UTF8.GetBytes(message.ToJsonString() + "\n"), ct).ConfigureAwait(false);
                await Console.Out.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                writeGate.Release();
            }
        }

        _logger?.LogInformation("DeltaStudio MCP server ready on stdio ({ToolCount} tools)", _tools.All.Count);

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                break; // pipe closed
            }

            if (line is null)
            {
                break; // stdin EOF
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                await WriteAsync(JsonRpcMessage.Error(null, RpcError.ParseError, "Invalid JSON.")).ConfigureAwait(false);
                continue;
            }

            if (node is not JsonObject request)
            {
                await WriteAsync(JsonRpcMessage.Error(null, RpcError.InvalidRequest, "Expected a JSON object.")).ConfigureAwait(false);
                continue;
            }

            JsonNode? id = request.TryGetPropertyValue("id", out JsonNode? idNode) ? idNode?.DeepClone() : null;
            string method = request["method"]?.GetValue<string>() ?? string.Empty;
            JsonObject? args = request["params"] as JsonObject;

            JsonObject? response = await ProcessAsync(method, args, id, isNotification: id is null).ConfigureAwait(false);
            if (response is not null)
            {
                await WriteAsync(response).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Dispatches one MCP request; null for notifications. Exposed for in-process tests and future
    /// HTTP/SSE transports (the same dispatcher, different framing).
    /// </summary>
    public async Task<JsonObject?> ProcessAsync(string method, JsonObject? args, JsonNode? id, bool isNotification = false)
    {
        try
        {
            switch (method)
            {
                case "initialize":
                    return Respond(id, Initialize(args));

                case "notifications/initialized":
                    _initialized = true;
                    return null;

                case "ping":
                    return Respond(id, new JsonObject());

                case "shutdown":
                    return Respond(id, new JsonObject());

                case "tools/list":
                    return Respond(id, new JsonObject { ["tools"] = _tools.ToListPayload() });

                case "tools/call":
                    return Respond(id, await CallToolAsync(args).ConfigureAwait(false));

                case "resources/list":
                    return Respond(id, PlcResources.List());

                case "resources/templates/list":
                    return Respond(id, PlcResources.ListTemplates());

                case "resources/read":
                    return Respond(id, await PlcResources.ReadAsync(_engine, args, _agentId).ConfigureAwait(false));

                case "prompts/list":
                    return Respond(id, PlcPrompts.List());

                case "prompts/get":
                    return Respond(id, PlcPrompts.Get(args));

                default:
                    if (isNotification)
                    {
                        return null; // unknown notifications are ignored per spec
                    }

                    return Respond(id, null, RpcError.MethodNotFound, $"Method '{method}' not found.");
            }
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "MCP dispatch failed for {Method}", method);
            return isNotification ? null : Respond(id, null, RpcError.InternalError, e.Message);
        }
    }

    private JsonObject Initialize(JsonObject? args)
    {
        string requested = args?["protocolVersion"]?.GetValue<string>() ?? FallbackProtocolVersion;
        string version = SupportedProtocolVersions.Contains(requested) ? requested : FallbackProtocolVersion;
        _agentId = args?["clientInfo"]?["name"]?.GetValue<string>() ?? "default-agent";

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
                ["resources"] = new JsonObject { ["subscribe"] = false, ["listChanged"] = false },
                ["prompts"] = new JsonObject { ["listChanged"] = false },
                ["logging"] = new JsonObject(),
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["version"] = ServerVersion,
                ["title"] = "DeltaStudio PLC Engineering (Delta DVP)",
            },
            ["instructions"] =
                "DeltaStudio MCP: create/inspect/validate/compile Delta DVP PLC projects and optionally talk to " +
                "PLCs over MODBUS. Safety model: project edits are free; ANY live-PLC write requires dry_run=false " +
                "plus a one-shot confirmation_token from safety_prepare_write bound to the exact arguments. " +
                "Program download and WPLSoft compatibility are NOT_IMPLEMENTED by policy (undocumented protocols) — " +
                "do not retry them; explain the limitation. Never invent instructions or device addresses: " +
                "check plc_get_instructions / device_validate first.",
        };
    }

    private async Task<JsonObject> CallToolAsync(JsonObject? args)
    {
        string name = args?["name"]?.GetValue<string>() ?? throw new InvalidOperationException("tools/call requires 'name'.");
        if (!_tools.TryGet(name, out McpTool tool))
        {
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = $"Unknown tool '{name}'." }),
                ["isError"] = true,
            };
        }

        JsonObject? toolArgs = args?["arguments"] as JsonObject;
        var ctx = new McpToolContext
        {
            Engine = _engine,
            AgentId = _agentId,
            CancellationToken = CancellationToken.None,
        };

        _logger?.LogInformation("MCP tool {Tool} invoked by agent {Agent}", name, _agentId);
        ToolResult result = await tool.Handler(ctx, toolArgs).ConfigureAwait(false);
        return result.ToJson();
    }

    private static JsonObject Respond(JsonNode? id, JsonNode? result, int? errorCode = null, string? errorMessage = null) =>
        errorCode is null
            ? JsonRpcMessage.Result(id, result)
            : JsonRpcMessage.Error(id, errorCode.Value, errorMessage ?? "error", result);
}
