# AgentPulse

[简体中文](README.md) | [English](README.en.md)

<img src="assets/app-icon.png" width="72" alt="AgentPulse icon">

面向 AI Agent 的桌面任务进度追踪工具，让用户直观看清任务做到哪一步、当前正在处理什么，以及是否需要确认或授权。将多个 Agent 的任务集中展示，提供分阶段进度、耗时和长时间未更新提醒，减少反复追问与切换对话。数据本地保存，不自动上传。

Windows 原生桌面任务进度组件，配套可由 Agent 调用的 `task-progress-visualizer` skill。组件使用 C#、Windows Forms 和 .NET Framework；不依赖 Node.js 或浏览器。

列表以消息卡片显示任务名称、进度条和耗时。点击查看需求、当前问题、模型与分段状态；右键管理置顶、历史、删除、尺寸和最小化。最小化为百分比圆环，跟踪队列中第一个未完成任务，支持直接拖动、滚轮缩放和自动保存。

**进度来自 Agent 自报，未经独立验证。** 计时和动画由组件本地运行；组件不会自行调用模型、读取隐藏推理，或从聊天结束推断任务完成。

组件启动后由用户自行关闭。Agent 不自动关闭或重启；没有任务时保持显示空状态，等待新的任务。

列表随任务内容自动收紧或增高，达到高度上限后滚动；少量任务不会占用多任务的大窗口。圆环恢复时按当前内容高度在附近展开。

## 安装与运行

需要 Windows 桌面、.NET Framework 编译器，以及 Windows PowerShell 5.1 或 PowerShell 7。Windows 的标准 Framework 编译器路径由构建脚本自动查找，不需要安装第三方包。

下载或克隆仓库后，在仓库目录执行：

```powershell
.\install.ps1
```

若系统提示禁止运行脚本，可仅为本次安装进程指定：`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1`。不需要永久修改系统策略。

安装脚本先编译并自检，再把组件安装到当前用户的 Documents/Codex/progress-widget，把 skill 安装到 Codex 技能目录。它不启动组件，也不关闭其他应用。更新前请先通过组件右键菜单关闭组件；安装保留已有任务及窗口设置。

安装后双击桌面的 **一键启动任务组件** 即可主动启动；安装目录还提供 `launch.cmd`，也可以直接双击 `ProgressWidget.exe`。已有实例时尝试激活现有窗口，不关闭或重启。使用 `install.ps1 -SkipDesktopShortcut` 可跳过桌面入口创建。

在支持 skills 的 Agent 中启用 `task-progress-visualizer` 后，符合条件的任务可由 Agent 新建并上报。没有桌面操作工具的 Agent 只能发出备用启动请求，不能保证窗口可见。

只想从源码运行：

```powershell
.\build.ps1 -SelfTest
.\ProgressWidget.exe
```

## 文档导航

|文档|用途|
|---|---|
|[使用说明](docs/USER_GUIDE.md)|列表、详情、圆环、调整与数据保存|
|[Agent 上报规范](docs/AGENT_REPORTING.md)|接入字段、汇报时机、长度、完成与异常处理|
|[Agent 开发交接](docs/AGENT_HANDOFF.md)|当前实现、代码地图、待办和禁止误操作事项|
|[维护与构建](docs/MAINTENANCE.md)|安装、自检、文件结构、数据权限和发布|
|[评估建议落实表](docs/EVALUATION_STATUS.md)|设计优化的实现情况与限制|
|[验证记录](docs/VERIFICATION.md)|已经验证的行为和仍需桌面验收的项目|
|[发布检查](docs/DELIVERY_CHECK.md)|上传包构建、安装和兼容性检查结果|
|[批量操作与待确认](docs/BULK_AND_CONFIRMATION.md)|清理范围、批量已阅和橙黄色暂停状态|

## 数据与上传

每个任务单独存为 `tasks/<task_id>.json`。已阅任务仍保留，只增加 `read_at` 标记。主列表按创建时间排序，最新在上。窗口、圆环、置顶分别保存为 `window.json`、`compact-window.json`、`pinned.txt`；旧的 `queue-order.json` 不再决定显示顺序。

**上传此仓库目录即可，不要直接上传正在使用的安装目录。** 本仓库没有真实任务、个人截图、日志、备份或用户窗口设置；示例均为虚构演示数据。`.gitignore` 也排除了运行产生的数据。

## 当前限制

- 列表卡片有浅色渐变和系统阴影，间隙透明；卡片没有真实的桌面 Acrylic 模糊。
- 圆环使用逐像素透明、渐变和抗锯齿绘制，列表则使用普通 Windows Forms 绘制。
- 动画与滚动做过优化，但没有测量 FPS，不承诺所有硬件和长列表都同样流畅。
- 置顶遵循普通 Windows 窗口机制，不能覆盖安全桌面或保证覆盖独占全屏应用。
- 最终圆环重启位置、连续滚动与部分菜单动画仍需桌面验收，详见验证记录。

不会或不知道或无法解决的就直接告诉我，不要提供虚假方案或信息。
