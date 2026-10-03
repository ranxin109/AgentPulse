# Agent 接入与上报

本文件面向调用组件的 Agent；开发维护交接见 [AGENT_HANDOFF.md](AGENT_HANDOFF.md)。安装后的简短调用入口位于 `skill/task-progress-visualizer/SKILL.md`。

## 什么任务接入

预计超过 2 分钟，或包含至少 3 个有意义步骤时建任务；短回答不建任务、不启动窗口、不上报进度。不要为了满足步数把简单操作人为拆分。用户明确要求追踪某任务时，遵从用户要求。

每个任务或 Agent 拥有独立、唯一且稳定的 task_id。继续同一任务复用原 ID；只修改自己的文件。组件仅接入主动调用 skill 的任务，不扫描所有聊天。

## 初始上报

```powershell
$scripts = Join-Path $env:USERPROFILE '.codex\skills\task-progress-visualizer\scripts'
$created = & "$scripts\new-task.ps1" -Title '示例任务' -Phases '收集信息','执行修改','检查结果' -Requirements '用户的实际要求' -Model '运行环境实际提供的模型或系列'
$taskId = $created.task_id
```

如果设置了 `CODEX_HOME` 或自定义 SkillDirectory，上例的 `$scripts` 应改为实际技能安装目录下的 `scripts`。

执行后记录返回的 ID 和路径。脚本生成阶段 `p1`、`p2` 等，仅第一阶段为 current，其他 pending。通过完整快照或后续 patch 可设置阶段权重、粗略预估和汇报间隔。无需每次阅读脚本源码。

## 必须提供什么

|字段|要求|
|---|---|
|`title`|短而明确的任务名|
|`requirements`|用户需求和约束，避免复制整个聊天|
|`model`|真实暴露的型号；仅知道系列就说明变体未知；不可推测|
|`status`|active、waiting、awaiting_confirmation、failed、completed|
|`current_problem`|一句话说明当前正在处理的问题或实际阻塞|
|`phases`|有序阶段，每段有稳定 id、name、status、progress|
|`report_interval_seconds`|根据预计持续时间选取，见下表|

阶段状态为 pending/current/waiting/awaiting_confirmation/completed；进度范围 0–100，pending 为 0、completed 为 100。每个阶段可有 `weight`（正有限数，默认 1）、note、真实时间戳和可选预估范围。组件和脚本按权重平均；旧数据没有 weight 时用预估中点，再缺失则等权。

`started_at`、`completed_at` 是实际事件时间，含时区偏移。脚本保留已有任务开始/已阅时间，自动写入本次报告时间与首次完成时间。首次 current/waiting 阶段自动补开始时间；不要捏造过去的阶段开始时间。预估只是粗略范围，不以修改预估掩盖超时。

## 什么时候提供、提供多少

|事件或任务规模|上报要求|
|---|---|
|开始|立即提供初始任务和计划|
|阶段切换、可证明的进展、阻塞、完成|立即提供变化|
|预计最多 5 分钟|静默间隔建议 60 秒|
|预计 5–30 分钟|建议 180 秒|
|预计超过 30 分钟|建议 480 秒|
|没有预估|先用 180 秒，必要时调整|

间隔表示 Agent 下次取得控制权时检查是否需要报告，不是额外定时唤醒模型。无新信息不发空心跳；长同步工具占用期间只能显示最后一次真实状态。组件本地计时、轮询、动画不消耗模型调用。

每次只写一句 current_problem，以及发生变化阶段的一句 note。不要重复所有不变字段、完整日志、任务无关信息或敏感数据。确认进度有可观察依据，再填写部分百分比。

## 增量更新与完成

把小型 patch 写为 UTF-8 JSON，再调用：

```powershell
& "$scripts\publish_progress_task.ps1" -TaskId $taskId -StatePath '.\patch.json' -Patch
```

```json
{
  "current_problem": "正在检查结果",
  "phase_updates": [
    {"id": "p1", "status": "completed", "progress": 100},
    {"id": "p2", "status": "current", "progress": 25, "note": "已有可验证的修改结果"}
  ]
}
```

顶层字段按名称覆盖，缺失字段保留；`phase_updates` 按稳定 id 合并。传入 `phases` 会替换整段数组。null 不表示删除。无 `-Patch` 是完整快照，不要误用。

需要用户确认、授权或必要澄清并因此暂停时，**在提问之前**提交任务和当前阶段的 `awaiting_confirmation`，在 current_problem 写清需要用户决定什么。组件显示橙黄色，暂停期间不因超时变红；用户回复后上报 active/current 再继续。进度不能因为等待而增加。示例：

```json
{"status":"awaiting_confirmation","current_problem":"请确认是否使用方案 A","phase_updates":[{"id":"p2","status":"awaiting_confirmation","note":"等待用户选择方案"}]}
```

仅可选问题、同时仍能继续执行时不必暂停整个任务。等待外部依赖用 waiting，实际不可继续的终止故障用 failed 并说明证据；不得仅按数据年龄报告 failed。

完成时把所有已结束阶段置 completed，再将任务 status 置 completed；脚本拒绝阶段尚未完成的任务完成报告。**完成报告必须在最终回复之前发布。** 组件不会因为聊天结束自动补完状态。中断后的未完成任务应保留并在后续续接，不能按年龄猜测完成。右键批量确认只标记已完成任务已阅，不代替 Agent 的完成上报。

## 启动、权限与并发

数据目录优先使用显式 `-TaskDirectory`，其次 `CODEX_PROGRESS_TASK_DIR`，否则默认 Documents/Codex/progress-widget/tasks。组件使用首个启动参数、同名环境变量或默认目录。只允许一个实例。目录改变需要用户自行关闭旧实例后再启动，Agent 不得自动关闭或重启。

组件启动后的关闭权属于用户。任务完成、全部已阅、清空、空列表、对话结束都保持运行；Agent 只启动或激活。部署/测试需要退出时也先准备更新，由用户自行关闭后生效，不得直接结束进程或代点关闭菜单。

桌面工具可用时按 Computer Use skill 查找组件、激活或启动，并截图验证。不可用时只发一次备用请求，明确无法验证可见性。skill 不能保证没有加载它的 Agent 自动接入。

发布通过锁与原子替换避免部分写入；已阅操作也使用同一文件锁。并发 Agent 使用不同任务 ID，父任务仅在收到子任务结果后更新汇总。错误 JSON 不应被静默覆盖。

进度是 Agent 自报，未经独立验证。不会或不知道或无法解决的就直接告诉我，不要提供虚假方案或信息。
