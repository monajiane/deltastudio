using DeltaStudio.Core.Devices;
using DeltaStudio.Protocols.Abstractions;
using DeltaStudio.Protocols.Modbus;

namespace DeltaStudio.Delta.Comm;

/// <summary>
/// Device access implemented over the standard MODBUS function codes plus the DVP mapping table.
/// This is the *only* way DeltaStudio talks to a running CPU today; program download is not
/// attempted (see <see cref="DeltaProgrammingProtocol"/>).
/// </summary>
public sealed class DvpDeviceAccess : IPlcDeviceAccess
{
    private readonly ModbusClient _client;
    private readonly DvpModbusAddressMap _map;

    /// <summary>Creates access over a connected client.</summary>
    public DvpDeviceAccess(ModbusClient client, DvpModbusAddressMap? map = null)
    {
        _client = client;
        _map = map ?? new DvpModbusAddressMap();
    }

    /// <summary>The mapping table in use (exposed for resources/diagnostics).</summary>
    public DvpModbusAddressMap Map => _map;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DeviceValue>> ReadBlockAsync(DeviceAddress start, int count, CancellationToken ct = default)
    {
        if (count <= 0)
        {
            return Array.Empty<DeviceValue>();
        }

        if (start.Kind is DeviceKind.D or DeviceKind.T or DeviceKind.C)
        {
            if (!_map.TryMapWord(start, out ModbusArea area, out int addr))
            {
                throw new NotSupportedException($"{start} has no MODBUS word mapping in the active table.");
            }

            if (area == ModbusArea.HoldingRegisters)
            {
                ushort[] words = await _client.ReadHoldingRegistersAsync((ushort)addr, (ushort)count, ct).ConfigureAwait(false);
                return words.Select(DeviceValue.FromWord).ToArray();
            }

            ushort[] input = await _client.ReadInputRegistersAsync((ushort)addr, (ushort)count, ct).ConfigureAwait(false);
            return input.Select(DeviceValue.FromWord).ToArray();
        }

        if (!_map.TryMapBit(start, out ModbusArea bitArea, out int bitAddr))
        {
            throw new NotSupportedException($"{start} has no MODBUS bit mapping in the active table.");
        }

        bool[] bits = bitArea == ModbusArea.Coils
            ? await _client.ReadCoilsAsync((ushort)bitAddr, (ushort)count, ct).ConfigureAwait(false)
            : await _client.ReadDiscreteInputsAsync((ushort)bitAddr, (ushort)count, ct).ConfigureAwait(false);
        return bits.Select(DeviceValue.FromBit).ToArray();
    }

    /// <inheritdoc/>
    public async Task WriteAsync(DeviceAddress address, DeviceValue value, CancellationToken ct = default)
    {
        if (!_map.IsWritable(address))
        {
            throw new UnauthorizedAccessException($"{address} is not writable through the MODBUS runtime channel.");
        }

        if (value.IsWord)
        {
            if (!_map.TryMapWord(address, out ModbusArea area, out int addr))
            {
                throw new NotSupportedException($"{address} has no MODBUS word mapping in the active table.");
            }

            if (area != ModbusArea.HoldingRegisters)
            {
                throw new NotSupportedException($"{address} is not writable via MODBUS on DVP (input register area).");
            }

            await _client.WriteHoldingRegisterAsync((ushort)addr, value.Word, ct).ConfigureAwait(false);
            return;
        }

        if (!_map.TryMapBit(address, out ModbusArea bitArea, out int bitAddr))
        {
            throw new NotSupportedException($"{address} has no MODBUS bit mapping in the active table.");
        }

        if (bitArea != ModbusArea.Coils)
        {
            throw new NotSupportedException($"{address} is not writable via MODBUS on DVP (discrete input area).");
        }

        await _client.WriteCoilAsync((ushort)bitAddr, value.Bit, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task WriteBlockAsync(DeviceAddress start, IReadOnlyList<DeviceValue> values, CancellationToken ct = default)
    {
        if (values.Count == 0)
        {
            return;
        }

        if (start.Kind != DeviceKind.D || values.Any(v => !v.IsWord))
        {
            throw new NotSupportedException("Block writes are only supported for D words.");
        }

        if (!_map.TryMapWord(start, out ModbusArea area, out int addr) || area != ModbusArea.HoldingRegisters)
        {
            throw new NotSupportedException($"{start} has no writable MODBUS word mapping.");
        }

        await _client.WriteHoldingRegistersAsync((ushort)addr, values.Select(v => v.Word).ToArray(), ct).ConfigureAwait(false);
    }
}
