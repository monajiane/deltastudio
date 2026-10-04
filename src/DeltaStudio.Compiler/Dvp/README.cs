namespace DeltaStudio.Compiler.Dvp;

/// <summary>
/// DESIGN NOTE: The Delta backend deliberately stops at the symbolic listing stage.
/// Delta's WPLSoft object code format and download framing are proprietary/undocumented,
/// and inventing byte encodings would be worse than an honest gap. A future verified encoder
/// implements <see cref="IDvpObjectCodeEncoder"/> and is injected here; the IR, validator and
/// every higher layer (UI, MCP) keep working unchanged.
/// </summary>
internal static class BackendDesignNote;
