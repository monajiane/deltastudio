using DeltaStudio.Core.Common;
using DeltaStudio.Core.Devices;
using DeltaStudio.Core.Model;
using DeltaStudio.Delta.Models;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Modbus;
using DeltaStudio.Protocols.Transport;

namespace DeltaStudio.Delta.Comm;

/// <summary>Settings for opening a DVP link.</summary>
public sealed record DvpLinkOptions
{
    /// <summary>Transport instance (serial, TCP-to-485 gateway or the mock).</summary>
    public required IPlcTransport Transport { get; init; }

    /// <summary>MODBUS framing: RTU or ASCII.</summary>
    public bool UseAscii { get; init; }

    /// <summary>Station number.</summary>
    public byte Station { get; init; } = 1;

    /// <summary>Override the address map when a site uses a custom table.</summary>
    public DvpModbusAddressMap? AddressMap { get; init; }
}

/// <summary>
/// Live DVP connection via documented MODBUS access. Honest capabilities:
///   identify          PARTIAL (FC07 flow control — community-documented framing, unverified on hardware)
///   read/write device SUPPORTED (standard MODBUS + mapping table)
///   run/stop          NOT_IMPLEMENTED (needs the programming protocol)
///   download/upload   NOT_IMPLEMENTED
/// </summary>
public sealed class DvpPlcLink : IPlcLink
{
    private readonly ModbusClient _client;
    private readonly IPlcTransport _transport;

    /// <summary>Runtime access to devices.</summary>
    public IPlcDeviceAccess Devices { get; }

    /// <summary>Underlying MODBUS client for diagnostics.</summary>
    public ModbusClient Client => _client;

    /// <summary>Programming-protocol seam (intentionally unimplemented).</summary>
    public IDeltaProgrammingProtocol Programming { get; } = new DeltaProgrammingProtocol();

    /// <summary>Creates a link over a configured transport.</summary>
    public DvpPlcLink(DvpLinkOptions options)
    {
        _transport = options.Transport;
        _client = new ModbusClient(_transport, options.UseAscii) { Station = options.Station };
        Devices = new DvpDeviceAccess(_client, options.AddressMap);
    }

    /// <summary>Opens (connects) the link.</summary>
    public Task ConnectAsync(CancellationToken ct = default) => _transport.ConnectAsync(ct);

    /// <inheritdoc/>
    public bool IsConnected => _transport.IsConnected;

    /// <inheritdoc/>
    public async Task<PlcIdentification> IdentifyAsync(CancellationToken ct = default)
    {
        (byte Station, ushort ModelCode, ushort Version)? result = await _client.FlowControlIdentifyAsync(10, ct).ConfigureAwait(false);
        if (result is not { } r)
        {
            return new PlcIdentification
            {
                Vendor = "Delta (unconfirmed)",
                VerifiedAgainstHardware = false,
                Notes = "FC07 flow-control response not understood/absent; identification is best-effort on DVP.",
            };
        }

        string? resolved = ResolveModel(r.ModelCode);
        return new PlcIdentification
        {
            Vendor = "Delta",
            ModelCode = r.ModelCode,
            Version = r.Version,
            ResolvedModelId = resolved,
            VerifiedAgainstHardware = false,
            Notes = $"Station {r.Station}. Model/version from FC07 framing documented by the community; " +
                    "DeltaStudio has not verified this framing against hardware — treat as advisory. " +
                    (resolved is null ? " Model code not in local catalog." : $" Local catalog match: {resolved}.")
        };
    }

    /// <summary>
    /// DVP RUN supervision relay M1000 (ON while the CPU runs) is documented in the programming
    /// manual's special-relay table; reading it through MODBUS is a legitimate, read-only way to
    /// tell RUN from STOP. Returns null when the read cannot be performed.
    /// </summary>
    public async Task<bool?> QueryRunStateAsync(CancellationToken ct = default)
    {
        try
        {
            IReadOnlyList<DeviceValue> values = await Devices
                .ReadBlockAsync(new DeviceAddress(DeviceKind.M, 1000), 1, ct).ConfigureAwait(false);
            return values.Count > 0 && values[0].Bit;
        }
        catch (Exception) when (ct.IsCancellationRequested == false)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken ct = default) => _transport.DisconnectAsync(ct);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// DVP FC07 model codes: Delta documents them per series in the "Modbus application" appendix,
    /// but the published tables are inconsistent across editions. Mapping is therefore data-driven
    /// and deliberately conservative — only codes with a single unambiguous community record are
    /// resolved. Extend DvpModelCodes.json (loaded from the app directory) when a code is verified.
    /// </summary>
    private static string? ResolveModel(ushort code) => DvpModelCodes.TryResolve(code);
}

/// <summary>Optional side-loaded map of verified FC07 model codes → catalog ids. Empty by default.</summary>
public static class DvpModelCodes
{
    private static readonly Dictionary<ushort, string> Map = Load();

    private static Dictionary<ushort, string> Load()
    {
        var map = new Dictionary<ushort, string>();
        string path = Path.Combine(AppContext.BaseDirectory, "DvpModelCodes.json");
        if (File.Exists(path))
        {
            try
            {
                var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (raw is not null)
                {
                    foreach ((string k, string v) in raw)
                    {
                        if (ushort.TryParse(k.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? k[2..] : k,
                                System.Globalization.NumberStyles.HexNumber, null, out ushort code))
                        {
                            map[code] = v;
                        }
                    }
                }
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or IOException)
            {
                // side-load is best effort
            }
        }

        return map;
    }

    /// <summary>Try to resolve a model code to a catalog id.</summary>
    public static string? TryResolve(ushort code) => Map.GetValueOrDefault(code);
}
