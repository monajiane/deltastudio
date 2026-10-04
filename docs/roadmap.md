# Roadmap / phase log

| phase | scope | status |
|---|---|---|
| 0 | solution, SDK pin, MIT, layered csproj graph | ✅ done |
| 1 | Core: IR, addresses, capabilities, validator, JSON format | ✅ done (38 tests) |
| 2 | Compiler: IL parse/format, listing-only DVP assembler + NOT_IMPLEMENTED encoder seam | ✅ done (10) |
| 3 | Protocols: transport abstraction, Modbus RTU/ASCII, mock PLC | ✅ done (12) |
| 4 | Delta layer: models from manual, catalog, map, link, programming-protocol refusal | ✅ done (54) |
| 5 | Infrastructure: engine composition, SQLite registry, safety tokens, monitor | ✅ done |
| 6 | Programming protocol (download/upload) | ⛔ NOT_IMPLEMENTED — blocked on documented Delta spec; checklist in [communication.md](communication.md) |
| 7 | MCP server: 51 tools / resources / prompts / safety classes | ✅ done (15) + stdio end-to-end verified |
| 8 | WinUI 3 app: shell, ladder editor, monitor, themes, fa/en+RTL | 🟡 code complete; Windows compile + interactive smoke pending |
| 9 | GUI polish: drag-drop wiring, live chip highlight, full fa string coverage | ⬜ |
| 10 | Hardware-in-the-loop validation; flip EXPERIMENTAL areas as evidence accumulates | ⬜ blocked: bench |
| 11 | WPLSoft adapter (only if a verified format description exists) | ⬜ policy-gated |
