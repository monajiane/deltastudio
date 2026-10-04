namespace DeltaStudio.Protocols.Transport;

/// <summary>Thrown on transport-level timeouts.</summary>
public sealed class TransportTimeoutException(string message) : TimeoutException(message);

/// <summary>Thrown when the port/device is unavailable.</summary>
public sealed class TransportException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>
/// Byte-frame transport. Implementations own framing granularity:
/// one call == one complete ADU. This keeps RTU inter-character-timeouts inside the transport
/// and out of the protocol logic (and makes the mock trivially swappable).
/// </summary>
public interface IPlcTransport : IAsyncDisposable
{
    /// <summary>True after ConnectAsync and before Dispose/Disconnect.</summary>
    bool IsConnected { get; }

    /// <summary>Opens the underlying resource.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Closes the underlying resource.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>Sends one complete request frame.</summary>
    Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default);

    /// <summary>Reads one complete response frame, or null on timeout.</summary>
    Task<byte[]?> ReadFrameAsync(CancellationToken ct = default);

    /// <summary>Diagnostic label for logs/UI (e.g. "COM3 9600 8E1 RTU").</summary>
    string Description { get; }
}
