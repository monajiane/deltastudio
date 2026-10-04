# Delta DVP devices & model capabilities

**Source of truth**: *DVP-ES2/EX2/SS2/SA2/SX2/SE & TP Operation Manual — Programming*
(Delta's official manual; fetched copy:
`https://deltronics.ru/images/manual/DVP-ES2-EX2-SS2-SA2-SX2-SE-TP_PM_EN_20181030.pdf`).
Everything below is encoded in `src/DeltaStudio.Delta/Models/DvpModelCatalog.cs`, and **every model
entry carries `Source` + `Verification` provenance in code** — currently `Partial`, because the
manual was consumed as extracted PDF text, page-by-page, not re-typeset by a human against paper.
Exact numbers were cross-checked against the SS2 series spec sheet where available.

## Device families (DVP general)

| device | addressing | range (documented) | notes |
|---|---|---|---|
| X (inputs) | **octal** | X0–X377 (per model width) | read-only at runtime; write refused by design |
| Y (outputs) | **octal** | Y0–Y377 (per model width) | DVP14SS2: X0–X7, Y0–Y5 (8 IN / 6 OUT) |
| S (state) | decimal | S0–S1023 | 0–19 general (initial) · 20–127 latched SFC states · 128–911 general · 912–1023 alarm |
| M (internal relay) | decimal | M0–M4095 | 0–511 general · 512–999 latched · 1000–1999 **special** (M1000 = RUN always-on, M1011 = 1 s clock) · 2000–2047 general · 2048–4095 latched |
| T (timer) | decimal | T0–T255 | 0–126 100 ms · 127 1 ms · 128–183 10 ms latched · 184–199 subroutine 1 ms · 200–239 1 ms latched · 240–245 accumulative 0.1 ms · 246–249 accumulative 1 ms · 250–255 special 1 ms — coil contact vs current-value word are distinct reads |
| C (counter) | decimal | C0–C261 | 0–111 16-bit up · 112–127 16-bit up/down · 200–219 32-bit up/down latched · 220–234 32-bit · 235–255 HSC 1-phase · 256–261 HSC 2-phase/AB |
| D (data) | decimal | per-model word capacity (SS2-class 5 k, SA2/SX2/SV2/SE 10 k words — see catalog `DataWordCapacity`) | words; K decimal constants / H hex constants |

Program capacity: **SS2-class 8 k steps (8 192) / SA2/SX2/SV2/SE 16 k steps (16 384)**; enforced as validation codes DS1013/DS1014. Latched link relays L0–L255 also modeled (`Partial` — SS2 availability needs hardware confirmation, note carried in code).

## Per-model I/O (in the catalog)

DVP14SS2 / DVP14SS2T = 8 in + 6 out; 16/24/32/48/64-point members per family; 14SS2T adds
transistor outputs + 4× 10 kHz HSC; RS-232 built-in, RS-485 expandable. See
`DvpModelCatalog` — each entry lists exact `InputPoints/OutputPoints`, capacities and the
capability flags the validator enforces.

## Modbus-visible device map (EXPERIMENTAL)

`DvpModbusAddressMap` (coils: S@0x0000, Y@0x0500, T-contacts@0x0600, M@0x0800, M2@0x0C00,
C@0x0E00, X-DI@0x0400; holding registers: D@0x1000, T/C words elsewhere) is assembled from
community knowledge-base sources, **not** hardware-verified → every area carries
`Verification.Experimental/Partial`. Tests assert internal consistency (no overlaps,
round-trips, permission matrix) — nothing more. Verify on real hardware before trusting in the field.
