param([string]$OutputPath = (Join-Path $PSScriptRoot 'ProgressWidget.exe'), [switch]$SelfTest)
$ErrorActionPreference = 'Stop'
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw '未找到 .NET Framework C# 编译器。' }
$sourceDir = Join-Path $PSScriptRoot 'src'
$sources = @(Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' | ForEach-Object { $_.FullName })
$iconPath = Join-Path $PSScriptRoot 'assets\app-icon.ico'
$iconOptions = @()
if (Test-Path -LiteralPath $iconPath) { $iconOptions += "/win32icon:$iconPath" }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 "/out:$OutputPath" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:Microsoft.VisualBasic.dll @iconOptions $sources
if ($LASTEXITCODE -ne 0) { throw "编译失败：$LASTEXITCODE" }
if ($SelfTest) {
    $check = Start-Process -FilePath $OutputPath -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
    if ($check.ExitCode -ne 0) { throw "自检失败：$($check.ExitCode)" }
    Write-Output 'SELF_TEST_OK'
}
Write-Output "已生成 $OutputPath"
