using DeltaStudio.Core.Program;

namespace DeltaStudio.Core.Project;

/// <summary>Bookkeeping metadata stored in project.json.</summary>
public sealed class PlcProjectMetadata
{
    /// <summary>Machine-readable project name.</summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>Human description.</summary>
    public string? Description { get; set; }

    /// <summary>Author string.</summary>
    public string? Author { get; set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Tool that last wrote the file.</summary>
    public string Tool { get; set; } = "DeltaStudio";

    /// <summary>Tool version string.</summary>
    public string ToolVersion { get; set; } = "0.1.0";
}

/// <summary>Serial line / station settings used to talk to the CPU.</summary>
public sealed class CommSettings
{
    /// <summary>OS port name (e.g. "COM3" on Windows, "/dev/ttyUSB0" on Linux).</summary>
    public string PortName { get; set; } = "COM3";

    /// <summary>Baud rate; Delta default for programming/MODBUS is 9600.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>None | Even | Odd. Delta factory default for MODBUS is Even.</summary>
    public string Parity { get; set; } = "Even";

    /// <summary>Stop bits (1 or 2).</summary>
    public int StopBits { get; set; } = 1;

    /// <summary>Data bits; DVP MODBUS is 8-N/E-1 style, fixed at 8.</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>"RTU" or "ASCII" MODBUS framing.</summary>
    public string Mode { get; set; } = "RTU";

    /// <summary>Per-request response timeout in milliseconds.</summary>
    public int TimeoutMs { get; set; } = 1000;
}

/// <summary>Selected CPU + communication target.</summary>
public sealed class PlcTarget
{
    /// <summary>PlcModelDefinition.Id of the selected CPU.</summary>
    public string ModelId { get; set; } = "DVP14SS2T";

    /// <summary>MODBUS station number (1..31 typically; Delta default 1).</summary>
    public int ModbusStation { get; set; } = 1;

    /// <summary>Serial settings.</summary>
    public CommSettings Communication { get; set; } = new();
}

/// <summary>
/// The complete in-memory project. This is THE shared object that both the WinUI editor and
/// the MCP server operate on — there is no second copy of the logic for either client.
/// </summary>
public sealed class PlcProject
{
    /// <summary>Metadata.</summary>
    public PlcProjectMetadata Metadata { get; set; } = new();

    /// <summary>Target CPU + comm.</summary>
    public PlcTarget Target { get; set; } = new();

    /// <summary>Program body.</summary>
    public PlcProgram Program { get; set; } = new();

    /// <summary>Symbol table.</summary>
    public List<SymbolDefinition> Symbols { get; set; } = new();

    /// <summary>Optional key/value project options (editor hints, future hardware config).</summary>
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bump modification timestamp.</summary>
    public void Touch() => Metadata.ModifiedUtc = DateTimeOffset.UtcNow;
}
