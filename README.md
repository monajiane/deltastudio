# DeltaStudio

**An open, production-oriented IDE alternative for Delta DVP PLCs — with a first-class MCP server
so AI agents can engineer the same projects the GUI edits.**

C# / .NET 8 · WinUI 3 · MVVM · SQLite · xUnit · MIT

```
src/DeltaStudio.Core          semantic ladder IR, device model, capability validation, open JSON format
src/DeltaStudio.Compiler      IL ⇄ IR, Delta symbolic listing (binary seam = NOT_IMPLEMENTED, on purpose)
src/DeltaStudio.Protocols     IPlcTransport, Modbus RTU/ASCII, in-process DVP mock PLC
src/DeltaStudio.Delta         DVP-SS2/SA2/SX2/SE/SV2/14SS2(T) models, instruction catalog, device map, link layer
src/DeltaStudio.Infrastructure engine, workspace, safety tokens, SQLite registry (locks/checkpoints), monitor
src/DeltaStudio.Mcp           51 tools, 8 resources, 8 prompts — JSON-RPC stdio, no GUI required
src/DeltaStudio.Cli           new · validate · compile · info · il · models · mcp-stdio
src/DeltaStudio.App           WinUI 3 IDE: tree · ladder editor · monitor · output/diagnostics · dark/light · en/fa(RTL)
tests/                        130 tests (Core 38 · Compiler 10 · Protocol 12 · Delta 54 · MCP 16)
```

## Quick start

```bash
export PATH=$HOME/.dotnet:$PATH            # or your dotnet install
dotnet build DeltaStudio.sln               # WinUI app builds fully on Windows (shim elsewhere — docs/ui.md)
dotnet test  DeltaStudio.sln               # 130/130

DLL=src/DeltaStudio.Cli/bin/Debug/net8.0/deltastudio.dll
dotnet $DLL new ./Motor Motor DVP14SS2T   # scaffold a project (dir, name, CPU model)
dotnet $DLL validate ./Motor               # capability + instruction checks
dotnet $DLL compile ./Motor                # symbolic Delta listing
dotnet $DLL mcp-stdio                      # ← point any MCP client at this (docs/mcp.md)
```

Building the WinUI 3 app on Windows additionally needs the Windows 10 SDK (XAML markup
compilation runs `XamlCompiler.exe`); the other projects and all tests build without it —
see docs/ui.md.

Try the shipped example: `examples/motor-start-stop` (X1-NC stop, (X0∥Y0) holding → Y0, X2→SET M10).

## What makes it different

- **Same engine, two thin clients.** The GUI and the MCP server drive one `DeltaStudioEngine`;
  agents and humans can't diverge on semantics (docs/architecture.md).
- **IR-first.** Ladder logic is a semantic node tree, not canvas rectangles; JSON format is open
  and documented (docs/project-format.md).
- **Honest industrial safety.** No invented Delta protocols: download/object-code/WPLSoft are
  explicitly `NOT_IMPLEMENTED` behind real abstractions (docs/communication.md); live writes need
  one-shot, digest-bound **confirmation tokens** (`safety_prepare_write`), reads are always safe.
- **Multi-agent aware.** SQLite project locks + auto-checkpoints/restore, tested cross-process.
- **Verified-capability model.** X/Y octal addressing, M1000 run relay, per-model capacities —
  each catalog entry carries its source and verification level (docs/devices.md).

## Docs & status

Start with **[docs/feature-status.md](docs/feature-status.md)** — every feature is marked
IMPLEMENTED / PARTIAL / EXPERIMENTAL / NOT_IMPLEMENTED, and this repo never marks a guess as done.

## License

MIT. Not affiliated with Delta Electronics; DVP, WPLSoft are their owners' marks.
