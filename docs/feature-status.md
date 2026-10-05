# Feature Status

Every user-visible feature carries an honest marker. When something is undocumented or
unverified against hardware, it is **not** silently faked — it is isolated behind an abstraction
and marked here (and in code via `CapabilityStatus`/`ProtocolStatus`).

Markers: **IMPLEMENTED** (works, tested) · **PARTIAL** (works with named gaps) ·
**EXPERIMENTAL** (usable but unverified against hardware/docs) · **NOT_IMPLEMENTED** (seam exists, refuses honestly).

## Core engine (`DeltaStudio.Core`)

| feature | status | notes |
|---|---|---|
| Semantic ladder IR (series/parallel/coil/instruction nodes, polarity, labels) | IMPLEMENTED | `Program/Nodes.cs`; layout is metadata only, never the model |
| Device address model (X/Y octal, M/S/T/C/D ranges, radix-aware) | IMPLEMENTED | `Devices/DeviceAddress.cs`; octal parsing verified against the DVP programming manual |
| Capability-checked validation (ranges per model, DS#### codes) | IMPLEMENTED | `Validation/`; 19 distinct codes (DS1001–DS1016, DS1090, DS2001–2002) incl. capacity DS1013/1014 and write-permission DS1010 |
| Open JSON project format (`project.json` / `program/main.json` / `symbols.json`) | IMPLEMENTED | documented in [project-format.md](project-format.md); schema-versioned with guards |
| Project file IO (atomic save, dirty tracking) | IMPLEMENTED | `ProjectFileIO` + `ProjectWorkspace` |
| Symbol table | IMPLEMENTED | name↔address, validated |
| Instruction catalog API (operands, validation, supported models, docs link) | IMPLEMENTED | `IInstructionCatalog` |
| Delta *binary* object-code encoding | NOT_IMPLEMENTED | format not published — see [communication.md](communication.md) and `IDvpObjectCodeEncoder` |
| WPLSoft `.prg` import | NOT_IMPLEMENTED | proprietary/undocumented; an honest adapter seam only, never claimed as "compatible" |

## Compiler (`DeltaStudio.Compiler`)

| feature | status | notes |
|---|---|---|
| IL parser (LD/AND/OR families + I/P/F polarity, ANB/ORB, SET/RST, TMR/CTR) | IMPLEMENTED | full round-trip with formatter under test |
| IL formatter (textual Delta IL from IR) | IMPLEMENTED | mnemonic table in `IlFormatter` |
| DVP symbolic listing / assembler front | PARTIAL | produces listing + step estimates; `BinaryEmittable=false` by policy with explicit `BinaryBlockedReason` |
| Optimizer | NOT_IMPLEMENTED | seam = IR→IL→IR; rung-merge heuristics are future work |

## Protocols (`DeltaStudio.Protocols`)

| feature | status | notes |
|---|---|---|
| `IPlcTransport` abstraction (connect/read/write/disconnect, timeouts) | IMPLEMENTED | serial + mock implement it; TCP/DVP-private would too |
| Modbus RTU + ASCII framing, CRC16/LRC | IMPLEMENTED | CRC vectors cross-checked independently |
| `ModbusClient` (FC01/03/05/06/15/16, FC07 identify, echo checks, inter-frame guard) | IMPLEMENTED | |
| Serial (RS-232/RS-485) transport | IMPLEMENTED | `System.IO.Ports`; hardware-in-the-loop validation still pending → **EXPERIMENTAL** against real CPUs |
| MockDvpPlc in-process emulator (X/Y/M/S/T/C/D, run state, ASCII/RTU) | IMPLEMENTED | test/dev only, always labelled "MOCK" in UI + MCP responses |
| Delta programming-port protocol (WPLSoft download/upload/run/stop) | NOT_IMPLEMENTED | `IDeltaProgrammingProtocol` + `ProtocolStatus.NotImplemented`; refuses every call; never guesses |

## Delta vendor layer (`DeltaStudio.Delta`)

| feature | status | notes |
|---|---|---|
| Model catalog (SS2, SA2, SX2, SE, SV2, 14SS2, 14SS2T) | IMPLEMENTED (data: Partial) | capacities/IO/ranges from the official programming manual — but manual pages were read as PDF text, so every model carries `Verification.Partial` |
| Capability-driven device ranges & program capacity | IMPLEMENTED | enforced in validator + editor |
| Instruction definitions (~55 core + application FNCs) | PARTIAL | mnemonics/operands from the manual; `EncodingVerified=false` on **all** (no invented opcodes; DRVA/DRVI marked Partial) |
| Modbus device map (S/Y/T/M/C/X/D areas) | EXPERIMENTAL | from community sources, cross-checked for internal consistency but **not** hardware-verified; per-area `Verification` flags |
| Write-permission model (X inputs read-only, D writable, etc.) | IMPLEMENTED | enforced in `DvpDeviceAccess` + mock |
| Run-state via M1000, advisory-only FC07 | PARTIAL | single-source mapping; documented |
| Object-code encoder for download | NOT_IMPLEMENTED | same policy as Core/Compiler |

## Infrastructure + MCP (`DeltaStudio.Infrastructure`, `.Mcp`)

| feature | status | notes |
|---|---|---|
| Engine composition (`AddDeltaStudioEngine`) shared by GUI + MCP | IMPLEMENTED | one engine, two thin clients |
| SQLite project registry (recents, checkpoints, locking) | IMPLEMENTED | WAL mode, capped history |
| Multi-agent project lock (`project_lock_*`) | IMPLEMENTED | owner + heartbeat; cross-process contention tested |
| Checkpoints + restore (`project_checkpoints`) | IMPLEMENTED | auto "pre-save" checkpoint on save |
| Safety tokens (prepare → confirm → write, one-shot, digest-bound, 2-min TTL) | IMPLEMENTED | refusal paths unit-tested incl. digest mismatch and reuse |
| 51 MCP tools / 8 resources / 1 template / 8 prompts | IMPLEMENTED | see [mcp.md](mcp.md); stdio JSON-RPC hand-rolled (SDK-independent) |
| `plc_monitor` streaming reads | PARTIAL | poll-based via `PlcMonitorService` (≥50 ms), not PLC-side forced read |
| `plc_program_download` tool | NOT_IMPLEMENTED | returns status + enablement checklist instead of frames |

## WinUI 3 app (`DeltaStudio.App`) — see [ui.md](ui.md)

| feature | status | notes |
|---|---|---|
| Shell: menus, toolbar, project tree, output/diagnostics/listing panes, status bar | IMPLEMENTED | compiles on Windows and passed a live launch smoke (window renders all four surfaces); automated GUI coverage NOT_IMPLEMENTED |
| Graphical ladder rendering from IR (contacts, coils, parallels, blocks, IL preview) | IMPLEMENTED (code) | real visual tree from semantic nodes — not a canvas of fixed rectangles |
| Editing: add/remove/move rungs, add contacts (NO/NC/UP/DOWN) and coils (OUT/SET/RST) | PARTIAL | parallel-branch insertion lives in the MCP/AI path (`ladder_add_contact parallel=true`) |
| Undo/redo (snapshot based) | IMPLEMENTED | |
| Connect: mock + serial (COM, 9600 7-E-1 default) | IMPLEMENTED (code) | serial = EXPERIMENTAL until hardware test |
| Online monitor panel (watch list @ 2 Hz default) | IMPLEMENTED (code) | live chip highlight not wired yet → PARTIAL display polish |
| Dark/light theme | IMPLEMENTED | |
| en/fa localization incl. RTL shell, LTR-pinned ladder | IMPLEMENTED (code) | catalog keys; not every string localized yet → PARTIAL coverage |
| WPF-style element drag-and-drop | NOT_IMPLEMENTED | honest gap; chips are tap/select + toolbar for now |
| Hardware configuration editor (expansion modules) | NOT_IMPLEMENTED | |
| WinUI build on non-Windows hosts | (shim) PARTIAL | solution builds everywhere; XAML compile runs only on Windows (toolchain constraint, `src/DeltaStudio.App/Directory.Build.props`) |

## Testing

| area | status | notes |
|---|---|---|
| Core (addresses/IR/validation/format) 38 tests | IMPLEMENTED | |
| Compiler (IL round-trips, parse failures) 10 tests | IMPLEMENTED | |
| Protocols (CRC/LRC vectors, timeouts, wrong-station silence, mock RTU/ASCII round-trips) 12 tests | IMPLEMENTED | |
| Delta (catalog data, maps, permissions, policy refusals) 54 tests | IMPLEMENTED | |
| MCP (protocol, annotations, workflows, safety flows, locks, checkpoints) 15 tests | IMPLEMENTED | |
| GUI automated UI tests | NOT_IMPLEMENTED | needs a Windows runner (WinAppDriver/Playwright) |
| Hardware-in-the-loop | NOT_IMPLEMENTED | no bench yet; all PLC behavior validated against the mock + documented maps |
