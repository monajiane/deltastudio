using System.IO.Ports;

namespace DeltaStudio.Protocols.Transport;

/// <summary>Serial configuration shared by RS-232/RS-485 wiring (physical layer agnostic).</summary>
public sealed record SerialTransportOptions
{
    /// <summary>Port name ("COM3" on Windows).</summary>
    public string PortName { get; init; } = "COM3";

    /// <summary>Baud rate.</summary>
    public int BaudRate { get; init; } = 9600;

    /// <summary>Data bits (DVP MODBUS: 7 or 8).</summary>
    public int DataBits { get; init; } = 8;

    /// <summary>Parity (DVP MODBUS default: Even).</summary>
    public Parity Parity { get; init; } = Parity.Even;

    /// <summary>Stop bits.</summary>
    public StopBits StopBits { get; init; } = StopBits.One;

    /// <summary>
    /// Extra quiet time (ms) appended to the 3.5-char inter-frame gap used to detect RTU end-of-frame.
    /// </summary>
    public int InterFrameGuardMs { get; init; } = 10;

    /// <summary>Response wait timeout.</summary>
    public int ReadTimeoutMs { get; init; } = 1000;
}

/// <summary>
/// RS-232/RS-485 transport over a serial port (works for USB-RS485 adapters too).
/// RS-485 direction control is delegated to the adapter/driver, which is the normal case on PC
/// hardware; software flow control of the DVP RS-485 pins is NOT implemented (see docs/communication.md).
/// </summary>
public sealed class SerialPortTransport : IPlcTransport
{
    private readonly SerialTransportOptions _options;
    private SerialPort? _port;

    /// <summary>Creates the transport for the given settings.</summary>
    public SerialPortTransport(SerialTransportOptions options) => _options = options;

    /// <inheritdoc/>
    public bool IsConnected => _port?.IsOpen == true;

    /// <inheritdoc/>
    public string Description => $"{_options.PortName} {_options.BaudRate} {_options.DataBits}{_options.Parity.ToString()[0]}{(int)_options.StopBits}";

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (_port is not null)
        {
            return Task.CompletedTask;
        }

        _port = new SerialPort(_options.PortName, _options.BaudRate, _options.Parity, _options.DataBits, _options.StopBits)
        {
            ReadTimeout = _options.ReadTimeoutMs,
            WriteTimeout = _options.ReadTimeoutMs,
        };
        _port.Open();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _port?.Close();
        _port?.Dispose();
        _port = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
    {
        EnsureOpen();
        await _port!.BaseStream.WriteAsync(frame, ct).ConfigureAwait(false);
        await _port.BaseStream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<byte[]?> ReadFrameAsync(CancellationToken ct = default)
    {
        EnsureOpen();
        Stream s = _port!.BaseStream;
        var buffer = new byte[512];
        int total = 0;

        // Frame end detection: the port goes quiet for the configured idle window.
        int idleMs = Math.Max(20, _options.InterFrameGuardMs);
        while (true)
        {
            byte[] chunk = await ReadWithTimeoutAsync(s, buffer.AsMemory(total, buffer.Length - total), idleMs, ct).ConfigureAwait(false);
            if (chunk.Length == 0)
            {
                return total == 0 ? null : buffer[..total];
            }

            total += chunk.Length;
            if (total >= buffer.Length)
            {
                return buffer;
            }
        }
    }

    private static async Task<byte[]> ReadWithTimeoutAsync(Stream s, Memory<byte> dest, int timeoutMs, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            int n = await s.ReadAsync(dest, cts.Token).ConfigureAwait(false);
            return n <= 0 ? Array.Empty<byte>() : dest[..n].ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Array.Empty<byte>(); // idle gap
        }
    }

    private void EnsureOpen()
    {
        if (_port is null || !_port.IsOpen)
        {
            throw new TransportException("Serial port is not open.");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
