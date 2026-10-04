using System.Text.Json.Nodes;
using DeltaStudio.Infrastructure;
using DeltaStudio.Infrastructure.Safety;
using DeltaStudio.Mcp;
using DeltaStudio.Mcp.Tools;
using Xunit;

namespace DeltaStudio.Mcp.Tests;

public sealed class McpHarness : IAsyncDisposable
{
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "ds-mcp-" + Guid.NewGuid().ToString("N"));
    public DeltaStudioEngine Engine { get; }

    public McpHarness()
    {
        Engine = new DeltaStudioEngine(dataDir: DataDir);
    }

    public async Task<JsonObject> RequestAsync(string method, JsonObject? args)
    {
        var server = GetServer();
        JsonObject? resp = await server.ProcessAsync(method, args, JsonValue.Create(1));
        Assert.NotNull(resp);
        return resp!;
    }

    private McpServer? _server;
    private McpServer GetServer() => _server ??= new McpServer(Engine);

    public async Task<ToolResult> CallAsync(string tool, JsonObject? args)
    {
        Assert.True(GetServer().Tools.TryGet(tool, out McpTool t), $"tool {tool} registered");
        var ctx = new McpToolContext { Engine = Engine, AgentId = "test-agent" };
        return await t.Handler(ctx, args);
    }

    public async ValueTask DisposeAsync()
    {
        await Engine.DisposeAsync();
        try { Directory.Delete(DataDir, true); } catch (IOException) { }
    }
}

public class ProtocolTests : IAsyncLifetime
{
    private readonly McpHarness _h = new();
    public async Task InitializeAsync()
    {
        JsonObject resp = await _h.RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "xunit-agent", ["version"] = "1.0" },
        });
        Assert.Equal("2024-11-05", resp["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("deltastudio", resp["result"]!["serverInfo"]!["name"]!.GetValue<string>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnknownMethod_IsJsonRpcError()
    {
        JsonObject resp = await _h.RequestAsync("frobnicate", null);
        Assert.NotNull(resp["error"]);
        Assert.Equal(-32601, resp["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task ToolsList_ExposesFullRequiredSurface()
    {
        JsonObject resp = await _h.RequestAsync("tools/list", null);
        var names = resp["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToHashSet();
        string[] required =
        [
            "project_create","project_open","project_save","project_close","project_get_info",
            "plc_select_model","plc_get_capabilities",
            "program_get","program_set",
            "rung_create","rung_get","rung_update","rung_delete",
            "ladder_add_contact","ladder_add_coil","ladder_add_branch","ladder_add_timer","ladder_add_counter",
            "instruction_add","instruction_update","instruction_delete",
            "device_validate","address_validate","project_validate",
            "compile_project","export_project","import_project",
            "connect_plc","disconnect_plc","plc_identify",
            "plc_read_device","plc_write_device","plc_read_register","plc_write_register",
            "plc_monitor","diagnostics_get",
        ];
        Assert.Subset(names, required.ToHashSet());
    }

    [Fact]
    public async Task SafetyClasses_AnnotatedOnToolsList()
    {
        JsonObject resp = await _h.RequestAsync("tools/list", null);
        var tools = resp["result"]!["tools"]!.AsArray();
        string ClassOf(string name) => tools.First(t => t!["name"]!.GetValue<string>() == name)!["_deltastudio_safety_class"]!.GetValue<string>();
        Assert.Equal("Safe", ClassOf("project_open"));
        Assert.Equal("Safe", ClassOf("plc_read_device"));
        Assert.Equal("Dangerous", ClassOf("plc_write_device"));
        Assert.Equal("Dangerous", ClassOf("plc_write_register"));
        Assert.Equal("Critical", ClassOf("plc_program_download"));
    }

    [Fact]
    public async Task PromptsList_And_Get()
    {
        JsonObject list = await _h.RequestAsync("prompts/list", null);
        var names = list["result"]!["prompts"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()).ToHashSet();
        foreach (string p in new[] { "create_plc_program", "review_plc_program", "debug_plc_program", "optimize_ladder", "explain_rung", "validate_delta_program", "prepare_download", "diagnose_plc" })
        {
            Assert.Contains(p, names);
        }

        JsonObject got = await _h.RequestAsync("prompts/get", new JsonObject
        {
            ["name"] = "create_plc_program",
            ["arguments"] = new JsonObject { ["goal"] = "X0 start X1 stop Y0 motor" },
        });
        string text = got["result"]!["messages"]!.AsArray()[0]!["content"]!["text"]!.GetValue<string>();
        Assert.Contains("X0 start X1 stop Y0 motor", text);
        Assert.Contains("safety_prepare_write", text);
        Assert.Contains("NOT_IMPLEMENTED", text); // must teach the refusal
    }

    [Fact]
    public async Task ResourcesList_And_ReadProgram()
    {
        await _h.CallAsync("project_create", new JsonObject { ["name"] = "res", ["model"] = "DVP14SS2T" });
        await _h.CallAsync("rung_create", null);
        await _h.CallAsync("ladder_add_contact", new JsonObject { ["rung"] = 0, ["device"] = "X0" });
        await _h.CallAsync("ladder_add_coil", new JsonObject { ["rung"] = 0, ["device"] = "Y0" });

        JsonObject read = await _h.RequestAsync("resources/read", new JsonObject { ["uri"] = "plc://current/program" });
        string text = read["result"]!["contents"]!.AsArray()[0]!["text"]!.GetValue<string>();
        Assert.Contains("LD X0", text);
        Assert.Contains("OUT Y0", text);

        JsonObject map = await _h.RequestAsync("resources/read", new JsonObject { ["uri"] = "plc://current/device-map" });
        Assert.Contains("0x1000", map["result"]!.ToJsonString());

        JsonObject rung = await _h.RequestAsync("resources/read", new JsonObject { ["uri"] = "plc://current/rung/0" });
        Assert.Contains("contact", rung["result"]!.ToJsonString());
    }

    [Fact]
    public async Task FullAiWorkflow_MotorStartStop()
    {
        // the docs/AI-workflow script, executed against the real tool handlers
        var create = await _h.CallAsync("project_create", new JsonObject { ["name"] = "Motor", ["model"] = "DVP14SS2T" });
        Assert.False(create.IsError);

        Assert.True(!(await _h.CallAsync("rung_create", new JsonObject { ["comment"] = "motor" })).IsError);
        Assert.True(!(await _h.CallAsync("ladder_add_contact", new JsonObject { ["rung"] = 0, ["device"] = "X1", ["polarity"] = "nc" })).IsError);
        Assert.True(!(await _h.CallAsync("ladder_add_contact", new JsonObject { ["rung"] = 0, ["device"] = "X0" })).IsError);
        Assert.True(!(await _h.CallAsync("ladder_add_contact", new JsonObject { ["rung"] = 0, ["device"] = "Y0", ["parallel"] = true })).IsError);
        Assert.True(!(await _h.CallAsync("ladder_add_coil", new JsonObject { ["rung"] = 0, ["device"] = "Y0" })).IsError);

        ToolResult vres = await _h.CallAsync("project_validate", null);
        Assert.False(vres.IsError);
        Assert.Contains("\"hasErrors\": false", vres.Content[0].Text);

        ToolResult cres = await _h.CallAsync("compile_project", null);
        Assert.Contains("OUT Y0", cres.Content[0].Text);
        Assert.Contains("not published", cres.Content[0].Text); // honest gating
    }

    [Fact]
    public async Task UnsupportedDevice_IsValidatedAgainstModel()
    {
        await _h.CallAsync("project_create", new JsonObject { ["name"] = "bad", ["model"] = "DVP14SS2T" });
        await _h.CallAsync("rung_create", null);
        var res = await _h.CallAsync("device_validate", new JsonObject { ["device"] = "X400" });
        Assert.Contains("OUT OF RANGE", res.Content[0].Text);

        var res2 = await _h.CallAsync("device_validate", new JsonObject { ["device"] = "X8" });
        Assert.Contains("INVALID FORMAT", res2.Content[0].Text);
    }

    [Fact]
    public async Task UnknownInstruction_IsRefused()
    {
        await _h.CallAsync("project_create", new JsonObject { ["name"] = "bad2", ["model"] = "DVP14SS2T" });
        await _h.CallAsync("rung_create", null);
        var res = await _h.CallAsync("instruction_add", new JsonObject { ["rung"] = 0, ["mnemonic"] = "TELEPORT", ["operands"] = new JsonArray() });
        Assert.True(res.IsError);
        Assert.Contains("refuses to invent", res.Content[0].Text);
    }
}
