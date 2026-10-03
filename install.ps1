param(
    [string]$InstallDirectory = (Join-Path $env:USERPROFILE 'Documents\Codex\progress-widget'),
    [string]$SkillDirectory,
    [switch]$SkipDesktopShortcut
)
$ErrorActionPreference = 'Stop'
if (!$SkillDirectory) {
    $codexRoot = $env:CODEX_HOME
    if (!$codexRoot) { $codexRoot = Join-Path $env:USERPROFILE '.codex' }
    $SkillDirectory = Join-Path $codexRoot 'skills\task-progress-visualizer'
}
$destination = [IO.Path]::GetFullPath($InstallDirectory)
$targetExe = Join-Path $destination 'ProgressWidget.exe'
foreach ($process in @(Get-Process -Name ProgressWidget -ErrorAction SilentlyContinue)) {
    try { $processPath = $process.Path } catch { throw 'Cannot inspect an existing widget process. Close the widget before installing.' }
    if ($processPath -and [string]::Equals($processPath, $targetExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Close the existing task widget before installing. The installer does not stop any applications.'
    }
}
$staging = Join-Path ([IO.Path]::GetTempPath()) ('progress-widget-install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$candidate = Join-Path $staging 'ProgressWidget.exe'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath $candidate -SelfTest
if (!(Test-Path -LiteralPath $candidate)) { throw 'Build did not produce the executable.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
New-Item -ItemType Directory -Path $SkillDirectory -Force | Out-Null
Copy-Item -LiteralPath $candidate -Destination $targetExe -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src') -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination $destination -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets') -Destination $destination -Recurse -Force
foreach ($name in @('build.ps1', 'build.cmd', 'launch.cmd', 'create-shortcut.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $destination -Force
}
$skillSource = Join-Path $PSScriptRoot 'skill\task-progress-visualizer'
foreach ($item in @(Get-ChildItem -LiteralPath $skillSource -Force)) {
    Copy-Item -LiteralPath $item.FullName -Destination $SkillDirectory -Recurse -Force
}
# Runtime task data and saved window settings are intentionally untouched.
$desktopShortcut = $null
if (!$SkipDesktopShortcut) {
    $desktopShortcut = & (Join-Path $PSScriptRoot 'create-shortcut.ps1') -WidgetPath $targetExe
}
[PSCustomObject]@{ WidgetPath = $targetExe; SkillDirectory = $SkillDirectory; DesktopShortcut = $desktopShortcut; BuildStaging = $staging }
