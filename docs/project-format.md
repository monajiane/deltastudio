# Project format (open, documented, JSON)

A DeltaStudio project is a **directory**:

```
MyProject/
├── project.json      metadata + target + options
├── program/main.json the ladder program (semantic IR, JSON-encoded)
└── symbols.json      symbol table
```

## project.json

(exact shape produced by `PlcProjectJson`; see `examples/motor-start-stop/project.json`)

```json
{
  "schemaVersion": 1,
  "kind": "DeltaStudio.Project",
  "metadata": {
    "name": "MotorStartStop",
    "description": "…",
    "author": "…",
    "createdUtc": "2026-10-04T00:00:00+00:00",
    "modifiedUtc": "2026-10-04T00:00:00+00:00",
    "tool": "DeltaStudio",
    "toolVersion": "0.1.0"
  },
  "target": {
    "modelId": "DVP14SS2T",
    "modbusStation": 1,
    "communication": {
      "portName": "COM3", "baudRate": 9600, "parity": "Even",
      "stopBits": 1, "dataBits": 8, "mode": "RTU", "timeoutMs": 1000
    }
  },
  "options": {}
}
```

`schemaVersion` is checked on load; unknown versions refuse to open (no silent best-effort parse).

## program/main.json — the semantic IR

Real file from `examples/motor-start-stop/program/main.json` (written verbatim by `PlcProjectJson`):

```json
{
  "schemaVersion": 1,
  "rungs": [
    {
      "number": 1,
      "comment": "Motor start/stop with holding contact. Stop is NC for fail-safe.",
      "logic": {
        "type": "series",
        "elements": [
          { "type": "contact", "device": "X1", "polarity": "nc", "label": "Stop" },
          {
            "type": "parallel",
            "branches": [
              { "type": "series", "elements": [ { "type": "contact", "device": "X0", "polarity": "no", "label": "Start" } ] },
              { "type": "series", "elements": [ { "type": "contact", "device": "Y0", "polarity": "no", "label": "Motor holding" } ] }
            ]
          },
          { "type": "coil", "device": "Y0", "action": "out", "label": "Motor Contactor" }
        ]
      }
    }
  ]
}
```

- Node types: `contact` (polarity `no|nc|up|down`), `coil` (action `out|set|rst`),
  `instruction` (mnemonic + operands array), `series`, `parallel`.
- Devices are canonical address strings (`X12` octal digits, `D100`, `K5`, `T3`…).
- `number` is display metadata; `label` per element is cosmetic. Neither participates in semantics —
  the same program compiled without labels is bit-identical in IR terms. Optional editor hints
  (row/column) are likewise ignored by validation/compile: the IR never needs them.

## symbols.json

```json
{
  "schemaVersion": 1,
  "symbols": [ { "name": "StartButton", "address": "X0", "comment": "NO start pushbutton" } ]
}
```

Symbols are validated like everything else (name collisions, address legality).

## WPLSoft interop — honest position

`status: NOT_IMPLEMENTED`. WPLSoft `.prg` files are proprietary and undocumented; nothing in
this repo claims compatibility. The plan (when/if undertaken): an **adapter** that either consumes
a *verified* public description of the format or converts losslessly from an intermediate
(WPLSoft-exported ASCII/IL, if its export proves parseable), marked `EXPERIMENTAL` until
round-trip-verified against WPLSoft itself. Until then: `import_project` accepts only the format
above. See [communication.md](communication.md).
