# Communication with real hardware — the honest state

## What works today

- **Modbus RTU/ASCII** framing (CRC16/LRC, inter-frame guard, echo checks, timeouts) — unit-tested
  with independently computed CRC vectors.
- **Serial transport** (RS-232/RS-485 adapter) via `System.IO.Ports`: 9600 7-E-1 default per
  Delta's MODBUS ASCII/RTU documentation of COM2, configurable per project.
- **DVP link layer** (`DvpPlcLink`): device read/write through the community Modbus map, run-state
  peek at M1000, FC07-style identification treated as **advisory** only (model codes cross-checked
  against two sources; a mismatch downgrades identification, never blocks).
- Everything above is marked **EXPERIMENTAL until hardware-in-the-loop passes** (no bench yet).

## What Delta does not publish — and what we do about it

The **programming-port protocol** WPLSoft uses (program download/upload, RUN/STOP over the
programming cable) and the **object-code encoding** of user programs are not documented by Delta.
Community reverse-engineering exists, but shipping guessed frames for industrial controllers is
how machines get bricked. DeltaStudio's policy, enforced in code:

| operation | behavior |
|---|---|
| `plc_program_download` (MCP) | `status: NOT_IMPLEMENTED` + 4-step enablement checklist in the response |
| `IDeltaProgrammingProtocol` (C#) | `Status.NotImplemented`; every call throws `DeltaProgrammingNotImplementedException` |
| `DvpAssembler` | emits symbolic listing only; `BinaryEmittable=false`, `BinaryBlockedReason` explains |
| `IDvpObjectCodeEncoder` | interface ships, implementation refused (`NotImplementedException`) |
| GUI compile | "compiled (listing only — object-code encoder is NOT_IMPLEMENTED)" |
| WPLSoft `.prg` | no compatibility claim anywhere; see project-format.md |

## Checklist to implement download for real

1. Obtain Delta's programming-port specification **or** capture WPLSoft↔CPU traffic you are
   authorized to analyze; document framing, key exchange, checksum semantics.
2. Implement `IDeltaProgrammingProtocol` + `IDvpObjectCodeEncoder` in `DeltaStudio.Delta`;
   flip `EncodingVerified` per instruction **only with evidence**.
3. Hardware-in-the-loop on a throwaway CPU: download → run → upload → byte-compare; power-loss
   resume; bad-checksum rejection.
4. Only then change status markers in [feature-status.md](feature-status.md), the compiler gate,
   and `plc_program_download` from `NOT_IMPLEMENTED` → `EXPERIMENTAL` → `IMPLEMENTED`.

Until step 3 passes, **no silent or partial download path exists anywhere in this codebase**
(tests assert the refusal).
