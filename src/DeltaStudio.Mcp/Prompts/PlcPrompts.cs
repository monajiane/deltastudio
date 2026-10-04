using System.Text.Json.Nodes;

namespace DeltaStudio.Mcp.Prompts;

/// <summary>
/// MCP prompts: guided playbooks that steer agents toward SAFE DeltaStudio usage. They are
/// deliberately tool-aware: each describes the exact tools to call and the refusals that are by design.
/// </summary>
public static class PlcPrompts
{
    private static readonly (string Name, string Desc, (string Name, string Desc, bool Required)[] Args, string Body)[] Catalog =
    [
        (
            "create_plc_program",
            "Create a new ladder program for a Delta DVP target following the safe workflow (model first, validate, never invent devices).",
            [("goal", "What the program must do, including I/O assignments (e.g. 'X0 start, X1 stop, Y0 motor with holding contact')", true),
             ("model", "Target model id (default DVP14SS2T)", false)],
            """
            GOAL: {goal}

            You are creating a Delta DVP PLC program in DeltaStudio via MCP tools that implements the GOAL above.
            Follow this workflow EXACTLY:

            1. Call plc_get_capabilities(model) FIRST. Only use device addresses inside the reported ranges
               (X/Y are OCTAL: X0..X7, X10..X17...). Never invent devices or instructions.
            2. project_create(name, model).
            3. Build rungs with rung_create + ladder_add_contact / ladder_add_coil / ladder_add_branch /
               ladder_add_timer / ladder_add_counter / instruction_add (instruction_add refuses unknown mnemonics;
               consult plc_get_instructions when unsure).
            4. Add human-readable run with rung_update comment: for every rung, state its purpose.
            5. project_validate MUST return hasErrors=false before you propose anything online.
            6. Show the user resources/read plc://current/program (IL text) so they can review the logic.
            7. STOP here and wait for the human's approval. Only then discuss transfer:
               - program DOWNLOAD is NOT_IMPLEMENTED (Delta's WPLSoft protocol/encoding are proprietary).
                 Do not attempt it, do not retry it; export_project instead and explain the manual transfer step.
               - live writes (plc_write_*) need safety_prepare_write → human confirmation → token, with dry_run=false.

            Common industrial patterns: start/stop with NC stop + holding contact; use SET/RST for latching
            across rungs; TMR K presets in the timer's base (100 ms T0-T126 on SS2: K50 = 5 s).
            """
        ),
        (
            "review_plc_program",
            "Act as an independent reviewer of the open project (safety + correctness), read-only.",
            [("focus", "Optional review focus (e.g. 'interlocks', 'outputs safety')", false)],
            """
            Review the currently open DeltaStudio project WITHOUT modifying it (you are reviewer; another agent may be author).
            1. resources/read plc://current/project (full IR) and plc://current/program (IL).
            2. project_validate — treat every Error/Warning seriously; explain each in plain language.
            3. Check specifically{focus_clause}: un-protected outputs, double coils, missing interlocks,
               SET without matching RST, timers with wrong time base, writes to X devices, out-of-range devices
               (device_validate each suspicious address against the model), and any use of devices that must be
               hardware-verified (extension X/Y beyond built-in count).
            4. Produce a findings table: severity, rung, issue, concrete suggested fix (tool calls to suggest, not execute).
            5. Do NOT call any write/mutation tool. Do NOT download. Close with overall verdict: SAFE-TO-PROCEED / NEEDS-FIXES / UNSAFE.
            """
        ),
        (
            "debug_plc_program",
            "Diagnose why a program misbehaves: static analysis first, then live monitoring (read-only).",
            [("symptom", "Observed behaviour (e.g. 'Y0 drops out after 5 s')", true)],
            """
            Debug the open project against the reported symptom: "{symptom}".
            1. Static pass: resources/read plc://current/project; project_validate; walk the rungs that write the
               affected output(s); look for double coils (DS1014), last-write-wins ordering, missing holding
               contacts, SET without RST, wrong NC/NO polarity, edge contacts used where level is needed.
            2. If a PLC is connected (diagnostics_get shows state): use plc_read_device + plc_monitor to watch
               the relevant X/M/T/C/Y set while reproducing the symptom; plc_read_register for data flow.
               Monitoring is read-only and always allowed.
            3. Explain root cause with evidence (addresses + observed values), propose the minimal edit set
               (rung_update/instruction_update calls), apply ONLY to the project (ProjectWrite), re-validate,
               and report before/after IL diff summary. Live fixes still need the confirmation flow.
            """
        ),
        (
            "optimize_ladder",
            "Reduce step count / simplify logic while preserving behaviour, with review.",
            [],
            """
            Optimize the open program's ladder logic:
            1. resources/read plc://current/project. Note each rung's purpose.
            2. Look for: duplicate series contacts, redundant NO/NC pairs in parallel (X + NC X = always ON),
               mergeable rungs, coils that can use SET/RST instead of duplicated logic, K constants that can
               be shared registers — ONLY semantics-preserving transforms.
            3. Write the optimized version with program_set (one rung at a time via rung_* if incremental is
               safer), then project_validate and compile_project: compare reported step estimate before/after.
            4. Present a per-rung change summary so the human can review semantics. Do NOT go online as part of
               optimization; transfer is a separate approved step.
            """
        ),
        (
            "explain_rung",
            "Explain one rung in plain language for an electrician, including timing/edge semantics.",
            [("rung", "0-based rung index", true)],
            """
            Explain rung #{rung} of the open project to an automation electrician:
            1. rung_get(rung={rung}) for the IR; render its IL via compile_project listing (grep the rung header).
            2. Describe in plain language: what conditions must hold, what devices change state, when; call out
               scan-cycle behaviour (OUT refreshed every scan vs SET latched), edge contacts fire for one scan,
               timer base (T0-T126 = 100 ms on SS2 class), counter reset conditions.
            3. List every device with its current model validity (device_validate) and flag anything that
               depends on unverified data (extension modules, L devices).
            4. End with a 'how to test on the bench' checklist (forcing inputs, monitoring outputs read-only).
            """
        ),
        (
            "validate_delta_program",
            "Full pre-transfer checklist for the open Delta DVP project.",
            [],
            """
            Run the DeltaStudio pre-transfer checklist on the open project:
            1. project_validate — zero errors required; list and explain every warning.
            2. plc_get_capabilities for the target model: re-check every device against ranges; run
               address_validate over the full device list extracted from the IR (every contact/coil/instruction operand).
            3. Confirm instruction usage: every instruction node's mnemonic in plc_get_instructions,
               operand shapes matching; remember machine ENCODING is unverified — download is not possible from
               DeltaStudio today; the checklist output is for a human transferring via WPLSoft or a future verified path.
            4. Capacity: step estimate vs model capacity (compile_project reports it).
            5. Output a PASS/BLOCKED report with reasons, and export_project to the review directory.
            """
        ),
        (
            "prepare_download",
            "Prepare what CAN be transferred safely today: export + human procedure (download itself is NOT_IMPLEMENTED).",
            [],
            """
            Prepare a 'download' for the open project within DeltaStudio's honest capabilities:
            1. compile_project: must have hasErrors=false; quote binaryBlockedReason verbatim to the user.
            2. plc_program_download exists but returns NOT_IMPLEMENTED — call it ONCE to show the user the
               official status and enabling path; NEVER attempt to bypass it (no hand-built frames, no
               'experimental' writes). This is a safety boundary, not a bug.
            3. export_project to a review folder and produce the human transfer procedure:
               open exported logic in WPLSoft (import compatibility is not guaranteed — retype/verify),
               checksum/visual review, PLC in STOP, machine cleared, then download from WPLSoft.
            4. If the user only needs runtime parameter changes: plc_write_device/register with
               safety_prepare_write → explicit human confirmation → token (dry_run=false) is allowed instead.
            """
        ),
        (
            "diagnose_plc",
            "Diagnose a connected PLC: identify, run state, communication health, key system registers.",
            [],
            """
            Diagnose the connected PLC (read-only throughout):
            1. diagnostics_get — transport + mock/hardware label. If offline: connect_plc first; never guess.
            2. plc_identify: report model code/version and its VERIFICATION caveat (FC07 framing is advisory, not
               hardware-verified). plc_run_state for RUN/STOP via M1000.
            3. Read (plc_read_device) the documented DVP system relays/registers relevant to faults on this family:
               M1000 (RUN supervision, always ON in RUN), M1011 (1-second clock pulse) and the special-M/special-D
               error devices named in the model's programming manual for the symptom at hand. Quote the manual
               entry you are relying on; state clearly which items are unverified on the specific CPU.
            4. Modbus comms health: note timeouts/exception codes seen; check parity/station config against D1120-class
               registers ONLY if the user confirms the model's comm-register layout (do not assume).
            5. Summarize: confirmed findings vs hypotheses; never write to the PLC during diagnosis.
            """
        ),
    ];

    private const string FocusClause =
        "{focus is empty ? '' : ' with extra focus on ' + focus, and additionally }";

    /// <summary>prompts/list payload.</summary>
    public static JsonObject List() => new()
    {
        ["prompts"] = new JsonArray(Catalog
            .Select(p => (JsonNode)new JsonObject
            {
                ["name"] = p.Name,
                ["description"] = p.Desc,
                ["arguments"] = new JsonArray(p.Args
                    .Select(a => (JsonNode)new JsonObject
                    {
                        ["name"] = a.Name,
                        ["description"] = a.Desc,
                        ["required"] = a.Required,
                    })
                    .ToArray()),
            })
            .ToArray()),
    };

    /// <summary>prompts/get payload (argument substitution on {name} placeholders).</summary>
    public static JsonObject Get(JsonObject? args)
    {
        string name = args?["name"]?.GetValue<string>() ?? throw new InvalidOperationException("prompts/get requires 'name'.");
        var entry = Catalog.FirstOrDefault(c => c.Name == name);
        if (entry.Name is null)
        {
            throw new ArgumentException($"Unknown prompt '{name}'.");
        }

        JsonObject? values = args?["arguments"] as JsonObject;
        string body = entry.Body;
        foreach ((string argName, _, _) in entry.Args)
        {
            string? supplied = values?[argName]?.GetValue<string>();
            body = body.Replace("{" + argName + "}", supplied ?? "(not supplied — ask the user)");
        }

        body = body.Replace(FocusClause, string.Empty);
        if (values?["focus"]?.GetValue<string>() is { Length: > 0 } focus)
        {
            body = body.Replace("Check specifically", "Check specifically — with extra focus on " + focus + " —");
        }

        body = body.Replace("{rung}", values?["rung"]?.GetValue<string>() ?? "0");
        body = body.Replace("{symptom}", values?["symptom"]?.GetValue<string>() ?? "(describe symptom)");

        return new JsonObject
        {
            ["description"] = entry.Desc,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = body },
            }),
        };
    }
}
