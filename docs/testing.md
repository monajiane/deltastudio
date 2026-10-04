# Testing

```bash
dotnet test DeltaStudio.sln     # 129 tests, 5 assemblies — no hardware needed
```

| assembly | tests | covers |
|---|---|---|
| `Core.Tests` | 38 | octal/decimal address parse & format, radix edge cases, IR⇄JSON round-trips incl. schema guards, every validation code path (ranges, capacity, permissions, symbols) |
| `Compiler.Tests` | 10 | IL parse/format round-trips (series, holding-contacts→parallel, ORB/ANB, edge polarity), malformed input rejection, coilless rung legality |
| `Protocol.Tests` | 12 | CRC16/LRC vectors (independently computed), ASCII framing + corruption rejection, timeout on dropped response, wrong-station silence, mock RTU/ASCII bit & word round-trips, input-write refusal, out-of-range errors, FC07 identify |
| `Delta.Tests` | 54 | catalog invariants (all models carry provenance), IO counts vs manual, capacity enforcement data, timer bands, instruction catalog (known/unknown/mnemonic integrity, MOV shape, no encoding claims), device-map (mapping, no overlap, reverse, permission matrix), programming policy types |
| `Mcp.Tests` | 15 | JSON-RPC handshake/-32601, tools/list annotation audit (safety classes), resources & prompts (incl. arg substitution), full AI workflow (create→edit→validate→compile→download refusal), dry-run default, token-required writes, one-shot + digest-bound tokens, checkpoint/restore, cross-process lock, register flow |

Philosophy: tests pin **documented facts and invariants** (e.g. `X17` = decimal 15; download always
refuses; X is never writable) rather than implementation trivia. `MockDvpPlc` is a *transport
simulator* over the same codec the serial client uses — so protocol bugs can hide nowhere between
"mock works" and "wire works"; what it cannot prove is hardware truth, which is why EXPERIMENTAL
labels exist. GUI tests (WinAppDriver) require a Windows runner → NOT_IMPLEMENTED.
