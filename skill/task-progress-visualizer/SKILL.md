---
name: task-progress-visualizer
description: Track tasks expected to exceed two minutes or involve at least three meaningful steps in the native Windows desktop progress widget. Skip short tasks.
metadata:
  short-description: Native desktop progress for substantial tasks
---

# Task Progress Visualizer

Only create a tracked task when it is expected to exceed two minutes OR has at least three meaningful steps. A continuing task reuses its existing ID. Short answers need no task, window launch, or progress report, even if a general AGENTS instruction loads this skill.

不会或不知道或无法解决的就直接告诉我，不要提供虚假方案或信息

## Start

Run `scripts/new-task.ps1 -Title <title> -Phases <ordered names> -Requirements <request> -Model <known model or family>`; it returns the task ID and file path. Keep both. Supply a unique `-TaskId` only when needed. Do not read script source just to run it. The script creates phase IDs `p1`, `p2`, etc.; only the first is current. Each agent owns a separate task ID.

Estimates are optional, rough ranges, never promises. Phase `weight` defaults to 1; overall progress is the weighted average. For older tasks without weights, estimate midpoints are used, falling back to equal weights. Do not invent partial progress or elapsed/completion times. The widget displays agent self-reports, not independently verified results.

Task files default to `%USERPROFILE%\Documents\Codex\progress-widget\tasks`. All scripts accept `-TaskDirectory` or `CODEX_PROGRESS_TASK_DIR`. The agent needs write permission there. The widget must read the same directory; pass it as its first startup argument when using a custom directory. Changing a running instance's directory requires the user to close it themselves first. Do not launch another instance repeatedly.

## Show the native widget

**User-owned lifetime:** once started, keep the widget running after task completion, acknowledgement, cleanup, an empty list or the end of a chat. Agents may start or activate it, but must never automatically close, kill, or restart it, including for testing, deployment or changing directories. Closing is a user action. Prepare updates without interrupting the running instance; if replacing a locked file requires exit, tell the user and wait for them to close it. Do not reopen after a user's closure unless starting a later qualifying task or explicitly asked.

When Windows Computer Use is available, read that skill once and use its `sky` API to find the existing ProgressWidget window and activate it, or launch `%USERPROFILE%\Documents\Codex\progress-widget\ProgressWidget.exe`. Verify a fresh screenshot shows the component. Use returned window objects. A process ID alone is not visibility evidence.

When Computer Use is unavailable, use `scripts/start_progress_widget.ps1` once and say visual visibility is unverified. If startup fails, provide concise chat progress; do not repeatedly retry or claim success. A script-only environment need not load the Computer Use instructions. A skill cannot guarantee execution in agents that do not load it.

## Report compact updates

Write a small UTF-8 JSON patch and run `scripts/publish_progress_task.ps1 -TaskId <id> -StatePath <patch path> -Patch`. Omit `-Patch` only for a full snapshot. Patch top-level fields by name and phases through `phase_updates`, using stable phase IDs. Example:

```json
{"current_problem":"Checking the resized layout","phase_updates":[{"id":"p1","status":"completed","progress":100},{"id":"p2","status":"current","progress":20,"note":"Build passed; desktop check pending"}]}
```

The publisher preserves original task start and existing read timestamps, stamps real update/completion times, validates state, and atomically writes the result. Completed task updates require all phases completed. Never finish another agent's task based only on age.

Report phase transitions, blockers, material progress and completion immediately. Choose `report_interval_seconds` by expected work: up to 5 minutes → 60; 5–30 minutes → 180; longer → 480. Without an estimate, use 180 and adjust if necessary. At return of control after this interval, report useful changes only. Do not create empty heartbeats or extra model calls. Timers and animations are local and spend no model tokens. Long blocking tools may prevent fresh reports.

Before requesting user confirmation, approval or necessary clarification that pauses execution, publish task `status: awaiting_confirmation` and the paused phase with the same status. Put the exact decision needed in `current_problem`, then ask the user. Keep the actual progress unchanged. The widget shows orange-yellow and excludes these tasks from timeout cleanup. After a reply, report `active` and phase `current` before resuming. For external blockers use `waiting`; for a proven terminal failure use `failed` with the actual reason, never infer failure from age. On completion, finish each phase and publish `status: completed`, with a concise outcome and verification limits. Do this before the final answer. For interrupted unfinished work, keep the task unfinished; resume the same ID later.

Active and waiting tasks turn red after no update for `max(120 seconds, 2 × report interval)`. Red means stale information, not proven failure. Explicit `failed` is also red. `awaiting_confirmation` stays orange-yellow until the agent resumes; completed tasks remain green. Bulk cleanup moves explicit failures and stale tasks to the Recycle Bin after a user confirmation, rechecking each file under its lock. Bulk acknowledgement marks completed tasks read with the normal grey/3-second exit animation; it never completes active tasks.

Read [the advanced reporting protocol](references/agent-reporting-protocol.md) only for concurrent agents, optional fields or recovery details.
