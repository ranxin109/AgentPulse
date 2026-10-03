param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Za-z0-9._-]{1,100}$')][string]$TaskId,
    [Parameter(Mandatory = $true)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$StatePath,
    [switch]$Patch,
    [string]$TaskDirectory = $env:CODEX_PROGRESS_TASK_DIR
)

$ErrorActionPreference = 'Stop'
if (!$TaskDirectory) { $TaskDirectory = Join-Path $env:USERPROFILE 'Documents\Codex\progress-widget\tasks' }
New-Item -ItemType Directory -Path $taskDirectory -Force | Out-Null
$target = Join-Path $taskDirectory ($TaskId + '.json')
$state = Get-Content -LiteralPath $StatePath -Raw -Encoding UTF8 | ConvertFrom-Json
$now = [DateTimeOffset]::Now.ToString('o')

function Set-Field($Object, [string]$Name, $Value) {
    if ($null -eq $Object.PSObject.Properties[$Name]) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    } else {
        $Object.$Name = $Value
    }
}

$lock = $null
$deadline = [DateTime]::UtcNow.AddSeconds(5)
while ($null -eq $lock) {
    try { $lock = [IO.File]::Open(($target + '.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
    catch [IO.IOException] { if ([DateTime]::UtcNow -ge $deadline) { throw }; Start-Sleep -Milliseconds 50 }
}
try {
$existing = $null
if (Test-Path -LiteralPath $target -PathType Leaf) {
    $existing = Get-Content -LiteralPath $target -Raw -Encoding UTF8 | ConvertFrom-Json
}

if ($state -isnot [pscustomobject]) { throw 'State must be a JSON object.' }
if ($Patch) {
    if (!$existing) { throw 'Patch target does not exist.' }
    $incoming = $state
    $state = $existing | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    foreach ($field in $incoming.PSObject.Properties) {
        if ($field.Name -ne 'phase_updates') { Set-Field $state $field.Name $field.Value }
    }
    foreach ($update in @($incoming.phase_updates | Where-Object { $null -ne $_ })) {
        if (!$update.id) { throw 'phase_updates requires a stable phase id.' }
        $matches = @($state.phases | Where-Object { $_.id -eq $update.id })
        if ($matches.Count -ne 1) { throw 'Unknown or duplicate phase id.' }
        foreach ($field in $update.PSObject.Properties) { Set-Field $matches[0] $field.Name $field.Value }
    }
}
if ($existing.started_at) { Set-Field $state 'started_at' $existing.started_at }
if ($state.status -eq 'completed' -and $existing.completed_at) { Set-Field $state 'completed_at' $existing.completed_at }
Set-Field $state 'task_id' $TaskId
if ([string]::IsNullOrWhiteSpace([string]$state.title)) { Set-Field $state 'title' '未命名任务' }
if ([string]::IsNullOrWhiteSpace([string]$state.requirements)) { Set-Field $state 'requirements' '未提供' }
if ([string]::IsNullOrWhiteSpace([string]$state.model)) { Set-Field $state 'model' '未提供' }
if ([string]::IsNullOrWhiteSpace([string]$state.status)) { Set-Field $state 'status' 'active' }
if ($state.status -notin @('active', 'waiting', 'awaiting_confirmation', 'failed', 'completed')) { throw "Unsupported task status: $($state.status)" }
if ([string]::IsNullOrWhiteSpace([string]$state.started_at)) {
    if ($existing -and -not [string]::IsNullOrWhiteSpace([string]$existing.started_at)) { Set-Field $state 'started_at' $existing.started_at }
    else { Set-Field $state 'started_at' $now }
}
Set-Field $state 'updated_at' $now
if ($null -eq $state.PSObject.Properties['completed_at']) { Set-Field $state 'completed_at' '' }
if ($state.status -eq 'completed' -and [string]::IsNullOrWhiteSpace([string]$state.completed_at)) { Set-Field $state 'completed_at' $now }
if ($state.status -ne 'completed') { Set-Field $state 'completed_at' '' }
if ($null -eq $state.PSObject.Properties['read_at']) { Set-Field $state 'read_at' '' }
if ($existing -and -not [string]::IsNullOrWhiteSpace([string]$existing.read_at)) { Set-Field $state 'read_at' $existing.read_at }

$phases = @($state.phases | Where-Object { $null -ne $_ })
if ($phases.Count -gt 0) {
    $low = 0.0
    $high = 0.0
    $estimatesKnown = $true
    $weightSum = 0.0
    $progressSum = 0.0
    foreach ($phase in $phases) {
        if ($phase.status -notin @('pending','current','waiting','awaiting_confirmation','completed')) { throw 'Invalid phase status.' }
        if ($phase.status -eq 'completed') {
            Set-Field $phase 'progress' 100
            if (!$phase.completed_at) { Set-Field $phase 'completed_at' $now }
        } elseif ($phase.status -eq 'pending') { Set-Field $phase 'progress' 0 }
        elseif (!$phase.started_at) { Set-Field $phase 'started_at' $now }
        if ($null -ne $phase.estimate_min_seconds -and ([double]$phase.estimate_min_seconds -lt 0 -or [double]$phase.estimate_max_seconds -lt [double]$phase.estimate_min_seconds)) { throw 'Invalid estimate range.' }
        if ($null -ne $phase.progress -and ([double]::IsNaN([double]$phase.progress) -or [double]::IsInfinity([double]$phase.progress))) { throw 'Nonfinite progress.' }
        if ($null -eq $phase.estimate_min_seconds -or $null -eq $phase.estimate_max_seconds) { $estimatesKnown = $false }
        else { $low += [double]$phase.estimate_min_seconds; $high += [double]$phase.estimate_max_seconds }
        $phaseLow = if ($null -eq $phase.estimate_min_seconds) { 1.0 } else { [double]$phase.estimate_min_seconds }
        $phaseHigh = if ($null -eq $phase.estimate_max_seconds) { $phaseLow } else { [double]$phase.estimate_max_seconds }
        $weight = if ($null -ne $phase.weight) { [double]$phase.weight } else { [Math]::Max(1.0, ($phaseLow + $phaseHigh) / 2.0) }
        if ($weight -le 0 -or [double]::IsNaN($weight) -or [double]::IsInfinity($weight)) { throw 'Weight must be finite and positive.' }
        $phaseProgress = if ($phase.status -eq 'completed') { 100.0 } elseif ($null -eq $phase.progress) { 0.0 } else { [Math]::Max(0.0, [Math]::Min(100.0, [double]$phase.progress)) }
        $weightSum += $weight
        $progressSum += $weight * $phaseProgress
    }
    $state.PSObject.Properties.Remove('estimated_total_min_seconds')
    $state.PSObject.Properties.Remove('estimated_total_max_seconds')
    if ($estimatesKnown) {
        Set-Field $state 'estimated_total_min_seconds' $low
        Set-Field $state 'estimated_total_max_seconds' $high
    }
    Set-Field $state 'progress' $(if ($state.status -eq 'completed') { 100 } elseif ($weightSum -gt 0) { [int][Math]::Round($progressSum / $weightSum) } else { 0 })
}

if ($state.status -eq 'completed') {
    if (@($phases | Where-Object { $_.status -ne 'completed' }).Count -gt 0) { throw 'Complete all phases before completing the task.' }
    Set-Field $state 'progress' 100
}
$temporary = $target + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
$backup = $target + '.' + [Guid]::NewGuid().ToString('N') + '.bak'
try {
    $json = $state | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $temporary -Value $json -Encoding UTF8
    if (Test-Path -LiteralPath $target -PathType Leaf) { [System.IO.File]::Replace($temporary, $target, $backup) }
    else { [System.IO.File]::Move($temporary, $target) }
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
}

} finally { $lock.Dispose() }
[pscustomobject]@{ task_id=$TaskId; path=$target; progress=$state.progress }
