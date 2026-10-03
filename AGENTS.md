# Project instructions

- Read `docs/AGENT_HANDOFF.md` before changing this project. For reporting-only use, read the installed task-progress-visualizer skill instead.
- Use the progress skill for substantive tasks expected to exceed two minutes or requiring at least three meaningful steps. Reuse a continuing task ID. Publish honest completion before the final response.
- Only `src/*.cs` participates in the native build. Preserve PowerShell 5.1 UTF-8 BOM compatibility.
- Keep real task JSON, user layouts, screenshots, logs and backups out of the repository. Use isolated synthetic data for checks.
- Do not operate or terminate unrelated apps. Desktop actions may target only the verified ProgressWidget window. Stop immediately when the user stops Computer Use.
- Do not close, kill or restart a running widget automatically, even for deployment or testing. The user closes it. Stage updates while it stays running; ask the user to close only if necessary to activate an update. Completion and an empty queue never close the component.
- Preserve UI and publishing contracts described in the handoff. Record actual verification, distinguish source changes from desktop observations, and keep unresolved items explicit.
- 不会或不知道或无法解决的就直接告诉我，不要提供虚假方案或信息。
