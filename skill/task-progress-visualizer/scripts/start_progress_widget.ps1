param(
    [string]$TaskDirectory = $env:CODEX_PROGRESS_TASK_DIR,
    [string]$StateFile,
    [string]$WidgetPath = (Join-Path $env:USERPROFILE 'Documents\Codex\progress-widget\ProgressWidget.exe')
)

$ErrorActionPreference = 'Stop'
if (!$TaskDirectory) { $TaskDirectory = Join-Path $env:USERPROFILE 'Documents\Codex\progress-widget\tasks' }
$widget = $WidgetPath
if (-not (Test-Path -LiteralPath $widget -PathType Leaf)) {
    throw "Native progress widget executable not found: $widget"
}

# Accept the old argument for callers that still pass the former single-task state file.
if (-not [string]::IsNullOrWhiteSpace($StateFile)) {
    $legacyPath = (Resolve-Path -LiteralPath $StateFile).Path
    $TaskDirectory = Join-Path (Split-Path -Parent $legacyPath) 'tasks'
}
if (-not (Test-Path -LiteralPath $TaskDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $TaskDirectory -Force | Out-Null
}
$resolvedTasks = (Resolve-Path -LiteralPath $TaskDirectory).Path

# Delegate GUI creation to Explorer so the window is created on the signed-in user's
# interactive desktop, rather than in a hidden command/tool session.
$shell = $null
try {
    $shell = New-Object -ComObject Shell.Application
    $shell.ShellExecute($widget, ('"{0}"' -f $resolvedTasks), (Split-Path -Parent $widget), 'open', 1)
} catch {
    throw "Could not launch the native progress widget on the interactive desktop: $($_.Exception.Message)"
} finally {
    if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
}
