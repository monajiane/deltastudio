using DeltaStudio.Core.Devices;
using DeltaStudio.Protocols.Abstractions;

namespace DeltaStudio.Protocols.Abstractions;

/// <summary>
/// Maps device addresses onto the four Modbus tables for one CPU family. This is the seam where
/// vendor-specific memory organization is isolated: the Delta defaults come from community
/// transcriptions of Delta's MODBUS manual and must be verified against hardware
/// (see DvpModbusAddressMap status flags).
/// </summary>
public interface IModbusDeviceMap
{
    /// <summary>Maps a bit device (contact view) to its coil/discrete-input location.</summary>
    bool TryMapBit(DeviceAddress device, out ModbusArea area, out int address);

    /// <summary>Maps a word device (D, or T/C present value) to its register location.</summary>
    bool TryMapWord(DeviceAddress device, out ModbusArea area, out int address);

    /// <summary>Reverse mapping used by simulators: Modbus location → device.</summary>
    bool TryResolveBit(ModbusArea area, int address, out DeviceAddress device);

    /// <summary>Reverse word mapping used by simulators.</summary>
    bool TryResolveWord(ModbusArea area, int address, out DeviceAddress device);

    /// <summary>True when writing this device via Modbus is meaningful (X inputs and T/C contacts are not).</summary>
    bool IsWritable(DeviceAddress device);

    /// <summary>True when the device's value spans two words (32-bit counter present values).</summary>
    bool IsDoubleWord(DeviceAddress device) => false;
}
