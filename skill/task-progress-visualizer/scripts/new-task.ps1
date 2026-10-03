param(
    [Parameter(Mandatory=$true)][string]$Title,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string[]]$Phases,
    [string]$Requirements = '',
    [string]$Model = '未提供（运行环境未暴露模型信息）',
    [ValidatePattern('^[A-Za-z0-9._-]{1,100}$')][string]$TaskId = ('task-' + [Guid]::NewGuid().ToString('N')),
    [string]$TaskDirectory = $env:CODEX_PROGRESS_TASK_DIR,
    [ValidateRange(30,3600)][int]$ReportIntervalSeconds = 180
)
$ErrorActionPreference = 'Stop'
$now = [DateTimeOffset]::Now.ToString('o')
$phaseList = @(); $i = 0
foreach ($name in $Phases) {
    $i++
    $phaseList += [ordered]@{ id="p$i"; name=$name; status=$(if ($i -eq 1) {'current'} else {'pending'}); progress=0; weight=1; started_at=$(if ($i -eq 1) {$now} else {''}); completed_at=''; note='' }
}
$state = [ordered]@{ title=$Title; requirements=$Requirements; model=$Model; status='active'; current_problem=$Phases[0]; phases=$phaseList; report_interval_seconds=$ReportIntervalSeconds }
$temp = [IO.Path]::GetTempFileName()
try {
    $state | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temp -Encoding UTF8
    & "$PSScriptRoot\publish_progress_task.ps1" -TaskId $TaskId -StatePath $temp -TaskDirectory $TaskDirectory
} finally { Remove-Item -LiteralPath $temp -Force }
