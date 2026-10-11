$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$panelOutput = Join-Path $repoRoot 'build\panel'
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
$testOutput = Join-Path $repoRoot 'build\tests'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
$baseRefs = @('System.dll','System.Core.dll','System.Web.Extensions.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) }
& $PSScriptRoot\build.ps1
& $compiler /nologo /utf8output /target:exe ('/out:' + (Join-Path $testOutput 'TimerEngineTests.exe')) $baseRefs (Join-Path $PSScriptRoot 'TimerEngine.cs') (Join-Path $PSScriptRoot 'TimerEngineTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Timer test build failed.' }
& (Join-Path $testOutput 'TimerEngineTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Timer tests failed.' }
$uiRefs = $baseRefs + (@('System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) })
$uiRefs += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path (Join-Path $frameworkDir 'WPF') $_) }
$uiSources = @('PanelApp.cs','TimerEngine.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /utf8output /target:exe /main:PanelIndependentQa ('/out:' + (Join-Path $testOutput 'PanelIndependentQa.exe')) $uiRefs $uiSources (Join-Path $PSScriptRoot 'tests\PanelIndependentQa.cs')
if ($LASTEXITCODE -ne 0) { throw 'Independent UI test build failed.' }
& (Join-Path $testOutput 'PanelIndependentQa.exe') $panelOutput
if ($LASTEXITCODE -ne 0) { throw 'Independent UI tests failed.' }
$smoke = Start-Process -FilePath (Join-Path $panelOutput '紫璃陪伴面板.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru -Wait
if ($smoke.ExitCode -ne 0) { throw 'UI smoke test failed.' }
Get-Content -LiteralPath (Join-Path $panelOutput 'ui-smoke-test.txt')
Write-Output '全部离线检查通过。'
