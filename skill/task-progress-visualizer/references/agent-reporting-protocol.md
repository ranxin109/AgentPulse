# Advanced reporting protocol

## Ownership and concurrency

Each task/agent has its own ID and file. Agents report only their own work. A parent may aggregate child outcomes only after receiving evidence. Use one publisher per task; the publisher serializes simultaneous agent writes with a per-file lock. Never delete or complete unrelated tasks.

`-Patch` replaces supplied top-level fields. `phase_updates` merges phase fields by unique `id`; supplying `phases` replaces the full phase list. Missing patch fields are preserved. Null does not mean deletion. Write JSON with UTF-8; scripts support Windows PowerShell 5.1 and include BOMs. Do not use an executable expression as task data.

## Fields

Required substantive information: title, requirements, actual model or exposed family, ordered named phases, current_problem and truthful status. Runtime model information may expose only a family: explicitly say the variant is unknown. Never infer a model from a task title or fabricate a version.

Task status: active, waiting, awaiting_confirmation, failed, completed. Phase status: pending, current, waiting, awaiting_confirmation, completed. awaiting_confirmation means the user must decide or grant approval before execution can continue: report it before asking, explain the decision in current_problem, preserve progress, and resume with active/current after receiving the reply. It stays orange-yellow and does not expire as a fault. failed is a proven terminal failure, not an inference from stale data. Phase progress is 0–100; pending means 0 and completed means 100. Weights are positive finite numbers, default 1. Estimate min/max seconds are optional; mark estimates as rough in user-facing descriptions. Do not change estimates to hide overruns. Total estimates are supplied only if all phases have ranges.

Start/completion timestamps represent real events with UTC offsets. New-task stamps the first phase start. The publisher stamps a newly current/waiting phase start and newly completed phase finish. Never invent past phase start times. Preserve the task ID and start time across resumed turns.

## Cadence and content budget

Use one short current_problem sentence and at most one short note for each changed phase. Unchanged phases need no repeated text. Completion reports contain outcome and actual verification limitations. Errors need the actionable cause and next step, not full logs or private data. Task-file polling does not invoke a model. The widget cannot wake an agent or observe hidden reasoning.

## Recovery and UI ownership

Malformed existing JSON is an error, not permission to overwrite. Correct it from a known good snapshot. Publisher lock acquisition times out after five seconds; stale `.lock` files themselves are harmless because ownership is an OS file handle. Atomic publishing prevents partial reads. UI read/archive and publisher writes should not be intentionally performed simultaneously; tasks should stop reporting after completion. Delete/read choices belong to the user. Existing acknowledged tasks stay acknowledged on future publication.

The native app is one instance per desktop session. Its lifetime belongs to the user: agents never automatically close, kill or restart it, even for tests, updates or an empty queue. If a directory change or deployment requires exit, stage the update and let the user close it themselves. Task completion never closes the component. Do not claim all-window priority over secure Windows screens or exclusive fullscreen applications; pinning uses normal Windows topmost behavior. Glasslike cards are a visual style; true live desktop blur is not currently implemented.
