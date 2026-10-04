using DeltaStudio.Compiler.Dvp;
using DeltaStudio.Core.Instructions;
using DeltaStudio.Core.Model;
using DeltaStudio.Core.Validation;
using DeltaStudio.Delta.Comm;
using DeltaStudio.Delta.Instructions;
using DeltaStudio.Delta.Models;
using DeltaStudio.Infrastructure.Monitoring;
using DeltaStudio.Infrastructure.Persistence;
using DeltaStudio.Infrastructure.Safety;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Mock;
using DeltaStudio.Protocols.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeltaStudio.Infrastructure;

/// <summary>An open PLC connection.</summary>
public sealed class PlcConnection
{
    /// <summary>The live link.</summary>
    public required IPlcLink Link { get; init; }

    /// <summary>Identification collected at connect time (advisory for DVP FC07).</summary>
    public PlcIdentification? Identification { get; set; }

    /// <summary>Transport description for the status bar.</summary>
    public required string TransportDescription { get; init; }

    /// <summary>True when running against the in-process emulator (never a real PLC).</summary>
    public bool IsMock { get; init; }
}

/// <summary>
/// The engine: composition root of the whole product. WinUI instantiates one; the MCP host
/// instantiates one; both drive identical logic. There is deliberately no other path to
/// project state, validation, compilation or PLC access.
/// </summary>
public sealed class DeltaStudioEngine : IAsyncDisposable
{
    private readonly ILogger<DeltaStudioEngine> _logger;

    /// <summary>Workspace (current project + mutation pipeline).</summary>
    public ProjectWorkspace Workspace { get; } = new();

    /// <summary>Instruction database (Delta backend).</summary>
    public IInstructionCatalog Catalog { get; } = new DvpInstructionCatalog();

    /// <summary>PLC model catalog.</summary>
    public IReadOnlyDictionary<string, PlcModelDefinition> Models => DvpModelCatalog.All;

    /// <summary>Validator (same instance used by UI, CLI, MCP).</summary>
    public ProjectValidator Validator { get; private set; }

    /// <summary>Delta assembler (IR → symbolic listing; binary gated).</summary>
    public DvpAssembler Assembler { get; private set; }

    /// <summary>Safety token issuer.</summary>
    public SafetyTokenService Safety { get; } = new();

    /// <summary>SQLite registry (recents, checkpoints, locks).</summary>
    public SqliteProjectRegistry Registry { get; }

    /// <summary>Active PLC connection or null.</summary>
    public PlcConnection? Active { get; private set; }

    /// <summary>Monitor for the active connection.</summary>
    public PlcMonitorService? Monitor { get; private set; }

    /// <summary>Raised when connection state changes (status bar / MCP notifications).</summary>
    public event EventHandler<string>? ConnectionChanged;

    /// <summary>Creates an engine.</summary>
    public DeltaStudioEngine(ILogger<DeltaStudioEngine>? logger = null, string? dataDir = null)
    {
        _logger = logger ?? NullLogger<DeltaStudioEngine>.Instance;
        Validator = new ProjectValidator(Catalog);
        Assembler = new DvpAssembler(Catalog, Validator);
        Registry = new SqliteProjectRegistry(dataDir);
    }

    /// <summary>Validates the open project against its target model.</summary>
    public ValidationReport Validate()
    {
        var project = Workspace.Current ?? throw new InvalidOperationException("No project open.");
        PlcModelDefinition model = ProjectWorkspace.ResolveModel(project.Target.ModelId);
        ValidationReport report = Validator.Validate(project, model);
        _logger.LogInformation("Validation finished: {Errors} errors, {Warnings} warnings",
            report.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error),
            report.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning));
        return report;
    }

    /// <summary>Assembles the open project (symbolic listing + gating info).</summary>
    public DvpAssemblyResult Compile()
    {
        var project = Workspace.Current ?? throw new InvalidOperationException("No project open.");
        PlcModelDefinition model = ProjectWorkspace.ResolveModel(project.Target.ModelId);
        return Assembler.Assemble(project, model);
    }

    /// <summary>Opens a link over an arbitrary transport (used by serial/USB connections).</summary>
    public async Task<PlcConnection> ConnectAsync(IPlcTransport transport, byte station, bool useAscii, CancellationToken ct = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        var link = new DvpPlcLink(new DvpLinkOptions
        {
            Transport = transport,
            Station = station,
            UseAscii = useAscii,
        });
        await link.ConnectAsync(ct).ConfigureAwait(false);
        PlcIdentification? id = null;
        try
        {
            id = await link.IdentifyAsync(ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Identify failed on connect");
        }

        Active = new PlcConnection { Link = link, Identification = id, TransportDescription = transport.Description };
        Monitor = new PlcMonitorService(link);
        ConnectionChanged?.Invoke(this, transport.Description);
        return Active;
    }

    /// <summary>Opens a link against the in-process emulator (explicitly labelled mock).</summary>
    public Task<PlcConnection> ConnectMockAsync(MockDvpPlc plc, bool useAscii = false, byte station = 1, CancellationToken ct = default)
    {
        IPlcTransport transport = new MockPlcTransport(plc);
        return ConnectAsync(new MockTransportDecorator(transport, "mock:" + plc.GetHashCode()), station, useAscii, ct);
    }

    /// <summary>Builds a mock emulator wired to the DVP map (for demos/tests without hardware).</summary>
    public MockDvpPlc CreateMockPlc(byte station = 1) =>
        new(new MockPlcOptions { Map = new DvpModbusAddressMap(), Station = station });

    /// <summary>Disconnects the active link.</summary>
    public async Task DisconnectAsync()
    {
        if (Active is null)
        {
            return;
        }

        Monitor?.Stop();
        Monitor?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        await Active.Link.DisposeAsync().ConfigureAwait(false);
        Active = null;
        Monitor = null;
        ConnectionChanged?.Invoke(this, "offline");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        await Registry.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class MockTransportDecorator : IPlcTransport
    {
        private readonly IPlcTransport _inner;
        public MockTransportDecorator(IPlcTransport inner, string label) { _inner = inner; Description = label; }
        public string Description { get; }
        public bool IsConnected => _inner.IsConnected;
        public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
        public Task DisconnectAsync(CancellationToken ct = default) => _inner.DisconnectAsync(ct);
        public Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default) => _inner.WriteAsync(frame, ct);
        public Task<byte[]?> ReadFrameAsync(CancellationToken ct = default) => _inner.ReadFrameAsync(ct);
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
