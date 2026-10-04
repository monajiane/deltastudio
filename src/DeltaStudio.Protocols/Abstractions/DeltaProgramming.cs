using DeltaStudio.Core.Common;

namespace DeltaStudio.Protocols.Abstractions;

/// <summary>Thrown by programming-protocol seams that are intentionally unimplemented.</summary>
public sealed class DeltaProgrammingNotImplementedException : NotSupportedException
{
    /// <summary>Creates with operation name.</summary>
    public DeltaProgrammingNotImplementedException(string operation)
        : base(
        $"Delta programming operation '{operation}' is NOT_IMPLEMENTED in DeltaStudio by policy. " +
        "The WPLSoft download/upload framing is proprietary and undocumented; DeltaStudio will not guess it. " +
          "Implement an IDeltaProgrammingProtocol against verified information to enable this. See docs/communication.md.")
    {
    }
}

/// <summary>
/// Seam for the *programming port protocol* (download/upload/run/stop over the USB/RS-232
/// programming link that WPLSoft uses). Kept abstract on purpose: the framing is not public.
/// A verified implementation plugs in without touching any other layer.
/// </summary>
public interface IDeltaProgrammingProtocol
{
    /// <summary>Overall implementation status — never fake this.</summary>
    ProtocolStatus Status { get; }

    /// <summary>Human-readable explanation of what is and isn't implemented.</summary>
    string StatusDetails { get; }

    /// <summary>Downloads a verified object image to the CPU. Requires an explicit confirmation token at the caller layer.</summary>
    Task DownloadProgramAsync(ReadOnlyMemory<byte> objectImage, CancellationToken ct = default);

    /// <summary>Reads back the program image from the CPU.</summary>
    Task<byte[]> UploadProgramAsync(CancellationToken ct = default);

    /// <summary>Puts the CPU in RUN.</summary>
    Task RunAsync(CancellationToken ct = default);

    /// <summary>Puts the CPU in STOP.</summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Reads the stored program checksum for comparison after download attempts.</summary>
    Task<uint?> ReadChecksumAsync(CancellationToken ct = default);
}
