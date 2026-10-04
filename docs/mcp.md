# DeltaStudio MCP Server

The MCP server is a **first-class client of the engine** — not an add-on bolted onto the GUI.
It runs headless (`deltastudio mcp-stdio`), so AI agents can drive projects while the WinUI app
is closed, open, or on another machine sharing the project directory.

## Run it

```bash
dotnet src/DeltaStudio.Cli/bin/Debug/net8.0/deltastudio.dll mcp-stdio [--dir <data-dir>]
```

Claude Desktop / Cursor / any MCP client config (absolute `dotnet` path recommended):

```json
{
  "mcpServers": {
    "deltastudio": {
      "command": "dotnet",
      "args": ["C:/tools/DeltaStudio/src/DeltaStudio.Cli/bin/Debug/net8.0/deltastudio.dll", "mcp-stdio"]
    }
  }
}
```

Protocol: JSON-RPC 2.0 over stdio, `initialize` / `tools/list` / `tools/call` /
`resources/list` / `resources/templates/list` / `resources/read` / `prompts/list` / `prompts/get`.
Unknown methods answer `-32601`; internal failures answer `-32603` **with the request id**.

## Safety model (hard rules)

Every tool is annotated with `_deltastudio_safety_class`:

| class | meaning | examples |
|---|---|---|
| `Safe` | read-only, cannot change project or PLC | `project_get_info`, `plc_read_device`, `diagnostics_get` |
| `Moderate` | changes the **project**, not the PLC | `rung_*`, `ladder_*`, `symbols_*`, `program_set` |
| `Dangerous` | changes a **live PLC** — requires `safety_prepare_write` → `confirmation_token` | `plc_write_device`, `plc_write_register` |
| `Critical` | can take a machine down — token **and** `dry_run=false` explicitly | `plc_run_stop_control`, `plc_program_download` |

Rules enforced in code (see `tools/PlcToolset.cs` and `Safety/`):

- `plc_write_*` with `dry_run=true` (the default) describe the change and touch nothing.
- A write with `dry_run=false` without a valid `confirmation_token` is **refused**.
- Tokens are issued by `safety_prepare_write(operation, canonical_args)`, are **one-shot**,
  expire after 2 minutes, and are bound to a SHA-256 digest of the canonical arguments —
  change the device or value and the token no longer matches.
- `plc_program_download` returns `NOT_IMPLEMENTED` with the path to enable it
  (see [communication.md](communication.md)); it never guesses frames.
- Multi-agent work: `project_lock_acquire/release/status` (SQLite-backed, with `agent_id`),
  and `project_checkpoints` before every save so any agent can audit or roll back.

## Tools (51)

| tool | class | summary |
|---|---|---|
| `address_validate` | Safe | Batch device address validation against a model (one result line per address) |
| `compile_project` | Safe | Validate + assemble to the Delta symbolic listing |
| `connect_plc` | Safe | Connect: transport='serial' (RS-232/485 via project or given settings) or transport='mock' (in-process emulator, clearly labelled, offline use only) |
| `device_validate` | Safe | Check whether one device address (e |
| `diagnostics_get` | Safe | Connection + workspace + safety + capability-status summary (everything read-only) |
| `disconnect_plc` | Safe | Close the PLC connection (does not change CPU state) |
| `export_project` | Safe | Save the current project to a directory in the documented open JSON format |
| `import_project` | Safe | Import a DeltaStudio project directory |
| `instruction_add` | ProjectWrite | Add a catalog application instruction block to a rung |
| `instruction_delete` | ProjectWrite | Delete any element (contact/coil/instruction) from the rung's top-level chain by index |
| `instruction_update` | ProjectWrite | Replace an instruction block in the rung's top-level chain (nodeIndex selects the element) |
| `ladder_add_branch` | ProjectWrite | Parallel structure: simple form ORs 'device' with the rung's last element; full form replaces the last element with a parallel of [existing-last, |
| `ladder_add_coil` | ProjectWrite | Add a coil (out/set/rst) at the end of the rung |
| `ladder_add_contact` | ProjectWrite | Add a contact (no/nc/rising/falling) in series; parallel=true ORs it with the last element (holding-circuit pattern) |
| `ladder_add_counter` | ProjectWrite | Add a CTR counter block (Cn + preset) |
| `ladder_add_timer` | ProjectWrite | Add a TMR timer block (Tn + preset) |
| `plc_get_available_models` | Safe | List all built-in PLC model ids |
| `plc_get_capabilities` | Safe | Full capability record for a model: device ranges, capacities, verification status |
| `plc_get_instructions` | Safe | Dump the instruction database (mnemonic, category, operand shapes, semantic status, encoding-verified flag) |
| `plc_identify` | Safe | Ask the connected CPU to identify itself (DVP: MODBUS FC07 flow control — community-documented framing; advisory until hardware-verified) |
| `plc_monitor` | Safe | Poll a bounded device list on a period and return all samples (read-only) |
| `plc_program_download` | Critical | Download object code to the CPU |
| `plc_read_device` | Safe | Read a contiguous block of same-kind devices from the connected PLC (read-only, safe) |
| `plc_read_register` | Safe | Read D data registers (word block) — read-only, safe |
| `plc_run_state` | Safe | Read the DVP RUN supervision relay M1000 through MODBUS (read-only) |
| `plc_run_stop_control` | Critical | Put the connected CPU in RUN or STOP |
| `plc_select_model` | ProjectWrite | Change the target CPU of the open project |
| `plc_write_device` | Dangerous | Write ONE device on a connected CPU |
| `plc_write_register` | Dangerous | Write D data registers (block) |
| `program_get` | Safe | The whole program as JSON IR ({ schemaVersion, rungs:[ {number, comment, logic} ] }) |
| `program_set` | ProjectWrite | Replace the entire program from JSON IR (same shape as program_get) |
| `project_checkpoint_restore` | ProjectWrite | Restore the workspace from a checkpoint snapshot (in-memory; review then project_save) |
| `project_checkpoints` | Safe | List automatic save checkpoints (latest 10) for rollback awareness |
| `project_close` | ProjectWrite | Close the current project (refuses when dirty unless force=true) |
| `project_create` | ProjectWrite | Create a new empty PLC project in memory (unsaved) |
| `project_get_info` | Safe | Project metadata, target, rung count and dirty flag as JSON |
| `project_lock_acquire` | ProjectWrite | Advisory per-project lock for multi-agent safety (lease-based, renew by re-acquiring) |
| `project_lock_release` | ProjectWrite | Release the advisory project lock (holder only) |
| `project_lock_status` | Safe | Who holds the project lock (expired leases report as free) |
| `project_open` | Safe | Open a DeltaStudio project directory (project |
| `project_recents` | Safe | Recent projects from the local registry |
| `project_save` | ProjectWrite | Save the current project (to its directory, or a new one) |
| `project_validate` | Safe | Run the full validator (same code path as the IDE's validation) |
| `rung_create` | ProjectWrite | Append (or insert at index) an empty rung, optionally with a comment |
| `rung_delete` | ProjectWrite | Delete a rung by index |
| `rung_get` | Safe | One rung as JSON ({number, comment, logic}), logic is the node JSON |
| `rung_move` | ProjectWrite | Reorder rungs (move a rung to a new index) |
| `rung_update` | ProjectWrite | Update rung comment and/or replace its logic wholesale (logicJson = series node JSON) |
| `safety_prepare_write` | Safe | Step 1 of a confirmed write |
| `symbol_upsert` | ProjectWrite | Add or update a named symbol bound to a device address |
| `symbols_get` | Safe | List all symbols |

## Resources

| uri | content |
|---|---|
| `plc://current/project` | metadata, model, target, stats |
| `plc://current/program` | program as Delta IL text |
| `plc://current/program/ir` | full semantic IR as JSON (what the editor edits) |
| `plc://current/diagnostics` | latest validation report |
| `plc://current/model` | model definition (the capability-checked one) |
| `plc://current/capabilities` | I/O counts, ranges, capacity |
| `plc://current/device-map` | Modbus register map with per-area verification status |
| `plc://current/instructions` | instruction catalog dump incl. verification flags |
| `plc://current/rung/{index}` | one rung as IR JSON (template resource) |

## Prompts (playbooks for agents)

`create_plc_program {goal}` · `review_plc_program` · `debug_plc_program {symptom}` ·
`optimize_ladder` · `explain_rung {index}` · `validate_delta_program` ·
`prepare_download` · `diagnose_plc`

Each prompt encodes the workflow order and the safety rules (e.g. `create_plc_program` tells the
agent to validate before finishing and that download is `NOT_IMPLEMENTED`), so a compliant client
gets the correct sequence even with a weak model driving it.

## Testing

`tests/DeltaStudio.Mcp.Tests` drives the same `ToolRegistry` the stdio transport uses
(protocol handshake, annotation audit on `tools/list`, resource/prompt round-trips, full
edit→validate→compile workflow, and the safety flows: refusal without token, one-shot use,
digest mismatch, cross-process lock contention, checkpoint/restore).
