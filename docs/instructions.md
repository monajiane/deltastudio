# Instruction database

`DvpInstructionCatalog` (~55 definitions in `src/DeltaStudio.Delta/Instructions/`) — the single
source the validator, IL parser, listing generator and MCP tools share. **No instruction
semantics are invented**: each definition is transcribed from Delta's programming manual and
carries:

- mnemonic, category (bit/word/arb/fcsr), operand list (types, optionality, ranges)
- supported model set (e.g. HSC-only instructions limited where documented)
- `Documentation` link (manual section)
- `EncodingVerified` — **false for all entries today**, because Delta's *object-code* encoding
  per instruction is not published. The catalog can therefore say "TMR K100 is legal and what its
  operands mean", and refuses to say "this compiles to bytes 0xXYZ". DRVA/DRVI carry partial
  notes (fnc 58/59 per community sources) flagged `Experimental`.

Unknown mnemonics are **refused** (`instruction_lookup`/`rung_update` reject them) rather than
accepted-and-ignored — tested.

## Adding an instruction

Add one `InstructionDefinition` (operands + validation + models + doc URL) and a parse case in
`IlParser` if the textual form is new. Everything else (GUI chips, MCP `instruction_*` tools,
listing, validation) picks it up through `IInstructionCatalog`. To *ever* emit binary, the missing
step is a verified encoder (`IDvpObjectCodeEncoder`), not a catalog hack — see
[communication.md](communication.md).
