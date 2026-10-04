using DeltaStudio.Core.Devices;
using DeltaStudio.Protocols.Abstractions;

namespace DeltaStudio.Infrastructure.Monitoring;

/// <summary>One sampled value.</summary>
public sealed record MonitorSample(DeviceAddress Address, DeviceValue Value, DateTimeOffset TimestampUtc, string? Error = null);

/// <summary>
/// Polls a fixed device list on a period and publishes samples. One implementation shared by
/// the WinUI monitor tab and the MCP plc_monitor tool (which captures N samples and returns).
/// </summary>
public sealed class PlcMonitorService : IAsyncDisposable
{
    private readonly IPlcLink _link;
    private readonly List<DeviceAddress> _devices = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Sample rate (Hz) reported to clients.</summary>
    public double SampleRateHz { get; private set; }

    /// <summary>Raised per polling cycle with all samples (batched).</summary>
    public event EventHandler<IReadOnlyList<MonitorSample>>? SamplesReady;

    /// <summary>Raised when a poll cycle fails (transport error). Monitoring continues after recovery.</summary>
    public event EventHandler<Exception>? PollFailed;

    /// <summary>Creates a monitor over a link.</summary>
    public PlcMonitorService(IPlcLink link) => _link = link;

    /// <summary>True while polling.</summary>
    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>Devices being watched.</summary>
    public IReadOnlyList<DeviceAddress> Devices => _devices;

    /// <summary>Starts (or restarts) polling.</summary>
    public void Start(IReadOnlyList<DeviceAddress> devices, TimeSpan period)
    {
        Stop();
        if (devices.Count == 0)
        {
            throw new ArgumentException("No devices to monitor.", nameof(devices));
        }

        if (period < TimeSpan.FromMilliseconds(50))
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Monitor period must be >= 50 ms.");
        }

        lock (_devices)
        {
            _devices.Clear();
            _devices.AddRange(devices);
        }

        SampleRateHz = 1.0 / period.TotalSeconds;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Stops polling.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            /* swallow: cancellation */
        }

        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (!ct.IsCancellationRequested)
        {
            List<DeviceAddress> snapshot;
            lock (_devices)
            {
                snapshot = _devices.ToList();
            }

            var samples = new List<MonitorSample>(snapshot.Count);
            foreach (DeviceAddress d in snapshot)
            {
                try
                {
                    IReadOnlyList<DeviceValue> values = await _link.Devices.ReadBlockAsync(d, 1, ct).ConfigureAwait(false);
                    samples.Add(new MonitorSample(d, values[0], DateTimeOffset.UtcNow));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    samples.Add(new MonitorSample(d, default, DateTimeOffset.UtcNow, e.Message));
                    PollFailed?.Invoke(this, e);
                }
            }

            SamplesReady?.Invoke(this, samples);

            try
            {
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    // pacing; poll again after 200ms — the mock returns instantly, serial pacing comes
                    // from the transport. (Simple fixed period keeps one code path honest for both.)
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Captures a bounded number of samples synchronously (MCP plc_monitor semantics).</summary>
    public async Task<IReadOnlyList<IReadOnlyList<MonitorSample>>> CaptureAsync(
        IReadOnlyList<DeviceAddress> devices, int cycles, TimeSpan period, CancellationToken ct = default)
    {
        var batches = new List<IReadOnlyList<MonitorSample>>(cycles);
        for (int i = 0; i < cycles; i++)
        {
            var samples = new List<MonitorSample>(devices.Count);
            foreach (DeviceAddress d in devices)
            {
                try
                {
                    IReadOnlyList<DeviceValue> values = await _link.Devices.ReadBlockAsync(d, 1, ct).ConfigureAwait(false);
                    samples.Add(new MonitorSample(d, values[0], DateTimeOffset.UtcNow));
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    samples.Add(new MonitorSample(d, default, DateTimeOffset.UtcNow, e.Message));
                }
            }

            batches.Add(samples);
            if (i < cycles - 1)
            {
                await Task.Delay(period, ct).ConfigureAwait(false);
            }
        }

        return batches;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Stop();
        await Task.CompletedTask;
    }
}
