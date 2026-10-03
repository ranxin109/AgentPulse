# AgentPulse

[简体中文](README.md) | [English](README.en.md)

<img src="assets/app-icon.png" width="72" alt="AgentPulse icon">

A desktop progress widget for AI agents. See how far a task has progressed, what the agent is working on, and when it needs your confirmation or permission. Bring tasks from multiple agents into one view, with phase progress, elapsed time, and alerts for stale updates. Data stays local and is not uploaded automatically.

AgentPulse is a native Windows application built with C#, Windows Forms, and .NET Framework. Its companion `task-progress-visualizer` skill lets participating agents create tasks and report progress. The widget does not require Node.js or a browser.

## Features

- **Compact task list:** message-style cards with task names, progress bars, and elapsed time. Newest tasks appear first.
- **Task details:** click a card to view the requirements, current issue, reported model, phases, statuses, and timing.
- **Progress states:** blue for ongoing work, green for completion, orange-yellow when waiting for user confirmation, and red for explicit failures or stale reports. A stale report does not prove that a task has failed.
- **Compact ring:** minimize the list to a gradient progress ring showing the first unfinished task in the queue. Drag it directly, resize with the mouse wheel, and save adjustments automatically.
- **Adaptive layout:** the list shrinks or grows with its contents, then scrolls without a visible scrollbar when it reaches its height limit. Restoring from the ring opens the list nearby using its current content height.
- **Context menu:** manage always-on-top behavior, history, deletion, size and position, and compact mode. Bulk actions acknowledge completed tasks or clear eligible failed/stale tasks after confirmation.
- **Local animations and timers:** these run in the widget without extra model calls.

**Progress is reported by the agent and is not independently verified.** The widget does not call models, read hidden reasoning, or infer completion when a conversation ends. Only tasks from agents that use the skill are tracked; it does not discover every agent's work automatically.

The user controls the widget's lifetime. Agents must not automatically close or restart it. With no tasks, it stays open and displays an empty state.

## Install and run

### Requirements

- A Windows desktop session.
- The .NET Framework compiler, which the build script locates in the standard Windows Framework directories.
- Windows PowerShell 5.1 or PowerShell 7.

No third-party packages are required. The application UI and detailed documentation are currently primarily in Chinese; this README provides an English introduction and setup guide.

Download or clone this repository, then run the following from its root directory:

```powershell
.\install.ps1
```

If script execution is blocked, use a policy override for this installation process only:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

You do not need to change the system execution policy permanently.

The installer compiles the application, runs its self-check, installs the widget into the current user's `Documents/Codex/progress-widget` directory, and installs the skill into the Codex skills directory. It does not start the widget or close other applications. Existing tasks and window settings are preserved during updates. Before updating an installed copy, close the widget yourself through its context menu.

After installation, double-click the desktop shortcut **一键启动任务组件** (Launch Task Widget). You can also use `launch.cmd` in the installation directory or double-click `ProgressWidget.exe`. If an instance already exists, the application attempts to activate its window without closing or restarting it.

To install without creating a desktop shortcut:

```powershell
.\install.ps1 -SkipDesktopShortcut
```

To build and run directly from source:

```powershell
.\build.ps1 -SelfTest
.\ProgressWidget.exe
```

## Agent integration

Enable `task-progress-visualizer` in an agent that supports skills. The skill tracks tasks expected to take more than two minutes **or** involve at least three meaningful steps. Short tasks do not need a tracking entry.

Each agent uses its own task ID and reports useful progress changes, phase transitions, blockers, and completion. Estimates are optional; overall progress uses phase weights, with equal weights as the default. Reporting frequency adapts to the expected task duration instead of creating extra model calls for empty heartbeats.

When work pauses for user confirmation or authorization, the agent reports `awaiting_confirmation`, shown in orange-yellow. For active or waiting tasks, reports become stale after `max(120 seconds, 2 × report interval)`. Completed tasks remain green, and tasks waiting for user confirmation are excluded from stale-task cleanup.

An agent without desktop-control tools can issue a fallback launch request, but it cannot guarantee that the window is visible on the user's desktop. A running process alone is not proof of visibility. Agents that do not load the skill cannot be relied upon to use it automatically.

See the [skill instructions](skill/task-progress-visualizer/SKILL.md) and [advanced reporting protocol](skill/task-progress-visualizer/references/agent-reporting-protocol.md) for the integration contract. These files are primarily in English; the detailed guides below are in Chinese.

## Documentation

| Document | Contents |
| --- | --- |
| [User guide](docs/USER_GUIDE.md) | Task list, details, compact ring, adjustments, and data storage |
| [Agent reporting](docs/AGENT_REPORTING.md) | Fields, timing, report length, completion, and error handling |
| [Agent handoff](docs/AGENT_HANDOFF.md) | Implementation, code map, remaining work, and operating rules |
| [Maintenance and builds](docs/MAINTENANCE.md) | Installation, checks, structure, permissions, and publishing |
| [Design improvements](docs/EVALUATION_STATUS.md) | Implementation status and limitations |
| [Verification](docs/VERIFICATION.md) | Completed checks and pending desktop acceptance work |
| [Release checks](docs/DELIVERY_CHECK.md) | Build, installation, compatibility, and publication scope |
| [Bulk actions and confirmation](docs/BULK_AND_CONFIRMATION.md) | Cleanup eligibility, bulk acknowledgement, and paused tasks |

## Local data and privacy

Each task is stored separately as `tasks/<task_id>.json`. Acknowledging a completed task adds a `read_at` marker; it does not delete its history. The main list sorts by creation time, newest first. Window geometry, compact-ring geometry, and pinning preferences are stored in `window.json`, `compact-window.json`, and `pinned.txt`. The legacy `queue-order.json` no longer controls the display order.

**Publish the source repository, not the installation directory you use for live tasks.** This repository contains no real task records, personal screenshots, logs, backups, or user window settings. The examples are fictional, and `.gitignore` excludes generated runtime data.

## Current limitations

- List cards use a light gradient and system shadow, with transparent gaps. They do **not** implement real desktop Acrylic blur.
- The compact ring uses per-pixel transparency, gradients, and antialiasing; the list uses standard Windows Forms rendering.
- Animation and scrolling have been optimized, but FPS has not been measured. Smoothness is not guaranteed across all hardware or long lists.
- Always-on-top follows normal Windows window behavior. It cannot cover the secure desktop and is not guaranteed to overlay exclusive fullscreen applications.
- Final acceptance checks are still needed for compact-ring placement after restart, continuous detail scrolling, and some menu animations. See the verification document for details.

If you do not know how to do something, lack the information, or cannot solve it, say so directly. Do not provide fabricated solutions or information.
