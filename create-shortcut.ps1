param(
    [string]$WidgetPath = (Join-Path $PSScriptRoot 'ProgressWidget.exe'),
    [string]$ShortcutDirectory = [Environment]::GetFolderPath('DesktopDirectory'),
    [string]$TaskDirectory = $env:CODEX_PROGRESS_TASK_DIR
)
$ErrorActionPreference = 'Stop'
$widget = (Resolve-Path -LiteralPath $WidgetPath -ErrorAction Stop).Path
if (!(Test-Path -LiteralPath $widget -PathType Leaf)) { throw 'Widget executable not found.' }
if ([string]::IsNullOrWhiteSpace($ShortcutDirectory)) { throw 'Desktop directory is unavailable. Specify ShortcutDirectory.' }
New-Item -ItemType Directory -Path $ShortcutDirectory -Force | Out-Null
$shortcutPath = Join-Path ([IO.Path]::GetFullPath($ShortcutDirectory)) '一键启动任务组件.lnk'
$shell = $null
$shortcut = $null
try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $widget
    $shortcut.WorkingDirectory = Split-Path -Parent $widget
    $shortcut.Arguments = ''
    if ($TaskDirectory) {
        if ($TaskDirectory.Contains('"')) { throw 'TaskDirectory cannot contain a quote.' }
        $shortcut.Arguments = '"' + [IO.Path]::GetFullPath($TaskDirectory) + '"'
    }
    $shortcut.Description = '启动任务组件；已运行时激活现有窗口，不关闭或重启。'
    $icon = Join-Path (Split-Path -Parent $widget) 'assets\app-icon.ico'
    $shortcut.IconLocation = if (Test-Path -LiteralPath $icon) { $icon + ',0' } else { $widget + ',0' }
    $shortcut.WindowStyle = 1
    $shortcut.Save()
} finally {
    if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) }
    if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
}
Write-Output $shortcutPath
