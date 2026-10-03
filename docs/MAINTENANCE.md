# 维护、构建和发布

## 仓库与安装目录

仓库只包含源代码、脚本、文档和虚构示例。安装目录存放实际任务与偏好。二者应分开，避免上传用户数据。默认安装路径为当前用户 Documents/Codex/progress-widget；skill 位于 Codex home 的 skills/task-progress-visualizer。

```text
src/                              原生 C# 源码
skill/task-progress-visualizer/    可安装 skill 与三个助手脚本
docs/                             使用、上报、交接和验证记录
examples/                         虚构状态及更新
tests/Test-Publisher.ps1           隔离目录中的发布脚本检查
build.ps1 / build.cmd              编译与数据/绘制自检
install.ps1                       编译、安装；不启动或关闭应用
launch.cmd                        用户双击启动入口
create-shortcut.ps1               创建桌面快捷方式，不启动组件
```

## 构建与检查

```powershell
.\build.ps1 -OutputPath '.\ProgressWidget.exe' -SelfTest
.\tests\Test-Publisher.ps1
```

编译器优先使用 Framework64/v4.0.30319/csc.exe，再回退到 Framework。`--self-test` 覆盖任务排序、已阅、权重、异常输入、时间、队列、圆环透明边缘、工作区限制和布局保存。发布测试使用独立临时目录，没有真实任务。

Windows PowerShell 5.1 兼容性要用其真正进程验证，不能把 PowerShell 7 的通过当成 5.1 的通过：

```powershell
& "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File '.\tests\Test-Publisher.ps1'
```

这些检查不验证窗口在用户桌面可见、真实动画流畅度或鼠标行为。桌面检查必须另行完成，并在验证记录里区分。

## 安装选项

```powershell
.\install.ps1 -InstallDirectory 'D:\MyWidget' -SkillDirectory 'D:\MySkills\task-progress-visualizer'
```

自定义组件安装目录后，Agent 应使用该目录下的可执行文件，或给备用启动脚本传 `-WidgetPath`。默认 skill 入口仍描述默认安装路径，应按本机配置调整调用。更新前由用户自行关闭当前组件；Agent 不得自动关闭或重启，安装脚本不终止任何进程。准备新版本不等于当前运行进程已经加载新代码。

安装默认创建“一键启动任务组件”桌面快捷方式，直接指向实际安装路径；`-SkipDesktopShortcut` 跳过。隔离安装检查也应使用此参数，避免修改真实桌面。`create-shortcut.ps1` 可指定 ShortcutDirectory 和 TaskDirectory，后者作为原生程序启动参数。不要提交生成的个人 `.lnk` 文件。

自定义**任务数据目录**与组件安装路径是不同设置：发布/新建使用 `-TaskDirectory`，组件启动首个参数传相同目录，或双方读取 `CODEX_PROGRESS_TASK_DIR`。显式路径优先。设定目录后由用户自行关闭旧实例，随后才能使用新目录启动。权限由当前环境决定，不保证自定义路径免审批。

组件窗口/圆环/排序设置位于任务目录的上一级；置顶设置位于可执行文件目录。选择自定义目录时注意两者可能不同。

## 发布助手语义

PowerShell 脚本使用 UTF-8 BOM，JSON 显式按 UTF-8 读取；发布使用临时文件原子替换，锁获取最多等待 5 秒。遗留 `.lock` 文件没有持续占锁能力，锁由文件句柄持有。

保持 StatePath 完整快照调用兼容；Patch 是显式选项。未知 phase id、无效权重、非有限进度、提前完成、损坏旧 JSON 都应错误退出。不要为了可发布而悄悄清空旧状态。

## GitHub 上传与后续发布

1. 只上传这个仓库目录，或解压交付 ZIP 后上传。
2. GitHub 页面看到的应是 README、src、skill、docs、examples、tests 和构建安装脚本。
3. 不上传安装目录中的 tasks、设置、日志、备份和截图。
4. 若制作二进制 Release，先完成验证文档中待验收的交互项目，再单独构建和附带验证说明。

项目许可证尚未选择，仓库作者可自行添加。
