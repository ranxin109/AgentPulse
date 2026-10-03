$ErrorActionPreference = 'Stop'
$scripts = Join-Path (Split-Path -Parent $PSScriptRoot) 'skill\task-progress-visualizer\scripts'
$dir = Join-Path ([IO.Path]::GetTempPath()) ('publisher-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
& "$scripts\new-task.ps1" -Title '中文任务测试' -Phases '阶段一','阶段二' -TaskDirectory $dir -TaskId sample -Model '测试模型' | Out-Null
$target = Join-Path $dir 'sample.json'
$before = Get-Content $target -Raw -Encoding UTF8 | ConvertFrom-Json
if ($before.title -ne '中文任务测试') { throw 'Chinese title round trip failed' }
$before.read_at = '2026-10-02T10:00:00+08:00'
$before | ConvertTo-Json -Depth 20 | Set-Content $target -Encoding UTF8
$patch = Join-Path $dir 'patch.data'
[IO.File]::WriteAllText($patch, '{"started_at":"2000-01-01T00:00:00Z","current_problem":"检查","phase_updates":[{"id":"p1","status":"completed","weight":1},{"id":"p2","status":"current","progress":20,"weight":3}]}', (New-Object Text.UTF8Encoding $false))
& "$scripts\publish_progress_task.ps1" -TaskId sample -StatePath $patch -Patch -TaskDirectory $dir | Out-Null
$after = Get-Content $target -Raw -Encoding UTF8 | ConvertFrom-Json
if ($after.progress -ne 40 -or $after.started_at -ne $before.started_at -or $after.read_at -ne $before.read_at -or $after.title -ne $before.title -or !$after.phases[0].completed_at -or !$after.phases[1].started_at) { throw 'Patch preservation/weight/timestamp failed' }
'{"status":"completed"}' | Set-Content $patch -Encoding UTF8
$rejected = $false
try { & "$scripts\publish_progress_task.ps1" -TaskId sample -StatePath $patch -Patch -TaskDirectory $dir | Out-Null } catch { $rejected = $true }
if (!$rejected) { throw 'Premature completion accepted' }
'{"status":"completed","phase_updates":[{"id":"p2","status":"completed"}]}' | Set-Content $patch -Encoding UTF8
& "$scripts\publish_progress_task.ps1" -TaskId sample -StatePath $patch -Patch -TaskDirectory $dir | Out-Null
$done = Get-Content $target -Raw -Encoding UTF8 | ConvertFrom-Json
if ($done.progress -ne 100 -or !$done.completed_at) { throw 'Completion failed' }
& "$scripts\publish_progress_task.ps1" -TaskId sample -StatePath $patch -Patch -TaskDirectory $dir | Out-Null
if ((Get-Content $target -Raw -Encoding UTF8 | ConvertFrom-Json).completed_at -ne $done.completed_at) { throw 'Completion timestamp changed' }
$env:CODEX_PROGRESS_TASK_DIR = $dir
& "$scripts\new-task.ps1" -Title '环境目录' -Phases '一步' -TaskId env-test | Out-Null
if (!(Test-Path (Join-Path $dir 'env-test.json'))) { throw 'Environment directory failed' }
'{"status":"awaiting_confirmation","current_problem":"请用户确认方案","phase_updates":[{"id":"p1","status":"awaiting_confirmation","progress":30}]}' | Set-Content $patch -Encoding UTF8
& "$scripts\publish_progress_task.ps1" -TaskId env-test -StatePath $patch -Patch -TaskDirectory $dir | Out-Null
$paused = Get-Content (Join-Path $dir 'env-test.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($paused.status -ne 'awaiting_confirmation' -or $paused.phases[0].status -ne 'awaiting_confirmation' -or $paused.progress -ne 30) { throw 'Confirmation state round trip failed' }
'{"status":"active","phase_updates":[{"id":"p1","status":"current"}]}' | Set-Content $patch -Encoding UTF8
& "$scripts\publish_progress_task.ps1" -TaskId env-test -StatePath $patch -Patch -TaskDirectory $dir | Out-Null
if ((Get-Content (Join-Path $dir 'env-test.json') -Raw -Encoding UTF8 | ConvertFrom-Json).status -ne 'active') { throw 'Resume confirmation failed' }
'{"status":"failed","current_problem":"真实故障说明"}' | Set-Content $patch -Encoding UTF8
& "$scripts\publish_progress_task.ps1" -TaskId env-test -StatePath $patch -Patch -TaskDirectory $dir | Out-Null
if ((Get-Content (Join-Path $dir 'env-test.json') -Raw -Encoding UTF8 | ConvertFrom-Json).status -ne 'failed') { throw 'Explicit failure state failed' }
Remove-Item Env:CODEX_PROGRESS_TASK_DIR
Write-Output ('PUBLISHER_TEST_OK PowerShell ' + $PSVersionTable.PSVersion + ' data=' + $dir)
