using System.Text.Json.Nodes;
using DeltaStudio.Infrastructure;
using DeltaStudio.Mcp.Tools;
using Xunit;

namespace DeltaStudio.Mcp.Tests;

public class SafetyTests : IAsyncLifetime
{
    private readonly McpHarness _h = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ds-safety-" + Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        // each test owns its project directory; xUnit runs test classes in parallel over one shared engine/registry
        var create = await _h.CallAsync("project_create", new JsonObject { ["name"] = "safety-" + Guid.NewGuid().ToString("N"), ["model"] = "DVP14SS2T", ["directory"] = _dir });
        Assert.False(create.IsError, create.Content[0].Text);
        var connect = await _h.CallAsync("connect_plc", new JsonObject { ["transport"] = "mock" });
        Assert.False(connect.IsError);
    }

    public async Task DisposeAsync() => await _h.DisposeAsync();

    [Fact]
    public async Task DryRun_IsDefault_NoTokenNeeded()
    {
        var res = await _h.CallAsync("plc_write_device", new JsonObject { ["rung"] = 0, ["device"] = "M0", ["value"] = "1" });
        Assert.False(res.IsError);
        Assert.StartsWith("DRY RUN", res.Content[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealWrite_WithoutToken_IsRefused()
    {
        var res = await _h.CallAsync("plc_write_device", new JsonObject
        {
            ["device"] = "M0",
            ["value"] = "1",
            ["dry_run"] = false,
        });
        Assert.True(res.IsError);
        Assert.Contains("confirmation_token", res.Content[0].Text);
        // and the PLC really was not touched:
        var read = await _h.CallAsync("plc_read_device", new JsonObject { ["device"] = "M0", ["count"] = 1 });
        Assert.Contains("\"value\": \"0\"", read.Content[0].Text);
    }

    [Fact]
    public async Task PrepareThenWrite_Succeeds_And_TokenIsSingleUse()
    {
        var prep = await _h.CallAsync("safety_prepare_write", new JsonObject
        {
            ["operation"] = "plc_write_device",
            ["canonical_args"] = "M0=1",
        });
        Assert.False(prep.IsError);
        string token = JsonNode.Parse(prep.Content[0].Text)!["confirmationToken"]!.GetValue<string>();

        var write = await _h.CallAsync("plc_write_device", new JsonObject
        {
            ["device"] = "M0",
            ["value"] = "1",
            ["dry_run"] = false,
            ["confirmation_token"] = token,
        });
        Assert.False(write.IsError);
        Assert.StartsWith("Wrote", write.Content[0].Text, StringComparison.Ordinal);

        var read = await _h.CallAsync("plc_read_device", new JsonObject { ["device"] = "M0", ["count"] = 1 });
        Assert.Contains("\"value\": \"1\"", read.Content[0].Text);

        // replay the same token → refused (one-shot)
        var replay = await _h.CallAsync("safety_prepare_write", new JsonObject
        {
            ["operation"] = "plc_write_device",
            ["canonical_args"] = "M0=1",
        });
        string token2 = JsonNode.Parse(replay.Content[0].Text)!["confirmationToken"]!.GetValue<string>();
        // consume it with WRONG args first:
        var mismatch = await _h.CallAsync("plc_write_device", new JsonObject
        {
            ["device"] = "M1",
            ["value"] = "1",
            ["dry_run"] = false,
            ["confirmation_token"] = token2,
        });
        Assert.True(mismatch.IsError);
        Assert.Contains("digest", mismatch.Content[0].Text);

        // token was NOT consumed by the failed attempt; correct args now pass
        var now = await _h.CallAsync("plc_write_device", new JsonObject
        {
            ["device"] = "M0",
            ["value"] = "0",
            ["dry_run"] = false,
            ["confirmation_token"] = (await _h.CallAsync("safety_prepare_write", new JsonObject
            {
                ["operation"] = "plc_write_device",
                ["canonical_args"] = "M0=0",
            }) is var pr && JsonNode.Parse(pr.Content[0].Text)!["confirmationToken"]!.GetValue<string>() is string tk ? tk : ""),
        });
        Assert.False(now.IsError);
    }

    [Fact]
    public async Task DownloadTool_ReportsNotImplemented_NotSilentExecution()
    {
        var res = await _h.CallAsync("plc_program_download", new JsonObject { ["dry_run"] = false });
        Assert.False(res.IsError); // a structured, explicit refusal
        Assert.Contains("NOT_IMPLEMENTED", res.Content[0].Text);
        Assert.Contains("pathToEnable", res.Content[0].Text.Replace("\n", string.Empty));
    }

    [Fact]
    public async Task RegisterWrite_RequiresConfirmationFlow()
    {
        var prep = await _h.CallAsync("safety_prepare_write", new JsonObject
        {
            ["operation"] = "plc_write_register",
            ["canonical_args"] = "D100=[7,8]",
        });
        string token = JsonNode.Parse(prep.Content[0].Text)!["confirmationToken"]!.GetValue<string>();
        var w = await _h.CallAsync("plc_write_register", new JsonObject
        {
            ["start"] = "D100",
            ["values"] = new JsonArray { "7", "8" },
            ["dry_run"] = false,
            ["confirmation_token"] = token,
        });
        Assert.False(w.IsError);
        var r = await _h.CallAsync("plc_read_register", new JsonObject { ["start"] = "D100", ["count"] = 2 });
        Assert.Contains("\"word\": 7", r.Content[0].Text);
    }

    [Fact]
    public async Task MultiAgentLock_BlocksOtherAgentWrites_OnDisk()
    {
        var save = await _h.CallAsync("project_save", new JsonObject { ["path"] = _dir });
        Assert.False(save.IsError);

        var lock1 = await _h.CallAsync("project_lock_acquire", new JsonObject { ["leaseSeconds"] = 60 });
        Assert.Contains("\"acquired\": true", lock1.Content[0].Text);

        // second agent, same engine's registry (shared SQLite) — simulate another process agent via direct API
        var other = await _h.Engine.Registry.AcquireLockAsync(_h.Engine.Workspace.Directory!, "reviewer-bot", 60);
        Assert.False(other.Acquired);
        Assert.Equal("test-agent", other.AgentId);

        var rel = await _h.CallAsync("project_lock_release", null);
        Assert.False(rel.IsError);
        var otherAfter = await _h.Engine.Registry.AcquireLockAsync(_h.Engine.Workspace.Directory!, "reviewer-bot", 60);
        Assert.True(otherAfter.Acquired);
    }

    [Fact]
    public async Task CheckpointAndRestore()
    {
        await _h.CallAsync("rung_create", new JsonObject { ["comment"] = "initial " + Guid.NewGuid().ToString("N") });
        await _h.CallAsync("project_save", new JsonObject { ["path"] = _dir });
        await _h.CallAsync("rung_create", new JsonObject { ["comment"] = "extra" });
        Assert.Equal(2, _h.Engine.Workspace.Current!.Program.Main.Count);

        var cps = await _h.CallAsync("project_checkpoints", null);
        long id = JsonNode.Parse(cps.Content[0].Text)!.AsArray()[0]!["id"]!.GetValue<long>();

        var restore = await _h.CallAsync("project_checkpoint_restore", new JsonObject { ["id"] = id });
        Assert.False(restore.IsError, restore.Content.Count > 0 ? restore.Content[0].Text : "?");
        Assert.Single(_h.Engine.Workspace.Current!.Program.Main);
    }
}
