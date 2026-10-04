using DeltaStudio.Core.Common;
using DeltaStudio.Protocols.Abstractions;

namespace DeltaStudio.Delta.Comm;

/// <summary>
/// The DeltaStudio position on the WPLSoft programming protocol, as code:
/// the abstraction is real, the implementation is NOT_IMPLEMENTED, and every call fails fast
/// with guidance instead of guessing frames. This is the seam a future, verified implementation
/// plugs into (PHASE 6 of the roadmap).
/// </summary>
public sealed class DeltaProgrammingProtocol : IDeltaProgrammingProtocol
{
    /// <inheritdoc/>
    public ProtocolStatus Status => ProtocolStatus.NotImplemented;

    /// <inheritdoc/>
    public string StatusDetails =>
        "Delta's programming-port protocol (used by WPLSoft for download/upload/run/stop) is not published by " +
        "Delta. Community reverse-engineering notes exist but are unverified; implementing them would silently " +
        "risk writing corrupt images to industrial controllers. DeltaStudio exposes the interface and refuses the " +
        "operations. To enable: capture and document the framing against hardware or official material, then " +
        "implement this interface. Program transfer can meanwhile be prepared offline (export_project)." ;

    private DeltaProgrammingNotImplementedException Refused(string op) =>
        new(op) { Data = { ["status"] = "NOT_IMPLEMENTED" } };

    /// <inheritdoc/>
    public Task DownloadProgramAsync(ReadOnlyMemory<byte> objectImage, CancellationToken ct = default) => throw Refused(nameof(DownloadProgramAsync));

    /// <inheritdoc/>
    public Task<byte[]> UploadProgramAsync(CancellationToken ct = default) => throw Refused(nameof(UploadProgramAsync));

    /// <inheritdoc/>
    public Task RunAsync(CancellationToken ct = default) => throw Refused(nameof(RunAsync));

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct = default) => throw Refused(nameof(StopAsync));

    /// <inheritdoc/>
    public Task<uint?> ReadChecksumAsync(CancellationToken ct = default) => throw Refused(nameof(ReadChecksumAsync));
}
