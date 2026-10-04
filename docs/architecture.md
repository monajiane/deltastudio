# Architecture

## Shape

```
DeltaStudio.App (WinUI 3)          DeltaStudio.Mcp (stdio JSON-RPC)
        │  thin: views + commands           │  thin: schema + annotations
        ▼                                   ▼
      DeltaStudio.Infrastructure  ◄── one composition, one engine ──►
      (engine, workspace, safety tokens,
       SQLite registry, monitor poller)
        │
        ├─► DeltaStudio.Core        semantic IR, addresses, validation, JSON format
        ├─► DeltaStudio.Compiler    IL ⇄ IR, DVP listing (binary = NOT_IMPLEMENTED seam)
        ├─► DeltaStudio.Protocols   IPlcTransport, Modbus RTU/ASCII, mock PLC
        └─► DeltaStudio.Delta       DVP models, instruction catalog, device map, link
```

Rules the code actually obeys:

- **WinUI and MCP are peers.** Both resolve the same `DeltaStudioEngine`
  (`AddDeltaStudioEngine`) and own **zero** engineering logic themselves. There is no GUI-only
  feature and no MCP-only feature.
- **Core knows nothing about Delta.** Vendor facts live in `DeltaStudio.Delta`; a second vendor
  plugs in beside it (models, catalog, map, link).
- **IR is semantic.** Rungs are `SeriesNetwork`/`ParallelNetwork`/node trees. Screen coordinates
  exist only as optional per-rung editor hints inside JSON; removing them loses nothing.
- **Unknown ⇒ abstraction, not invention.** Every undocumented area (object code, download
  protocol, WPLSoft files, hardware-timing) is an interface + `NOT_IMPLEMENTED` status + refusal
  message, marked in [feature-status.md](feature-status.md).
- Dangerous operations are *structurally* gated (safety tokens), not by convention.

## Notable mechanics

- `ProjectWorkspace.Mutate(action, reason)` — the single funnel for every edit (GUI, MCP, tests):
  version bump, dirty flag, event, so checkpoints/undo/refresh all hook one place.
- `SafetyTokenService` — one-shot 2-minute tokens bound to SHA-256 of canonical args; used by
  both `plc_write_*` tools and (by policy) any future direct-write UI.
- `SqliteProjectRegistry` (WAL) — recents, per-project checkpoints (capped), and multi-agent locks
  with owner/heartbeat; the GUI shows the same lock state agents see.
- `PlcMonitorService` — period-polling read batches (≥50 ms), shared by GUI monitor panel and
  `plc_monitor` MCP tool.
- Logging: `ILogger` throughout; the CLI pipes it to stderr, the GUI rings it into the Output tab
  (`UiLogService`), tests use `NullLogger`.

### What is "Clean Architecture"? — and why this instead

A layered, dependency-inward model (ports ≈ `IPlcTransport`, `IDvpObjectCodeEncoder`,
`IDeltaProgrammingProtocol`, `IInstructionCatalog`) without ceremony: no per-feature
use-case classes, no interface for its own sake. One engine aggregate, explicit seams only where
the outside world (OS serial, SQLite, PLC wiring, GUI toolkit) touches it. SOLID with a purpose —
see [feature-status.md](feature-status.md) for what each seam currently buys.
