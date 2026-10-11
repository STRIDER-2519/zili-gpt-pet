$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$panelOutput = Join-Path $repoRoot 'build\panel'
New-Item -ItemType Directory -Path $panelOutput -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $panelOutput 'assets') -Force | Out-Null
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'Requires 64-bit Windows with .NET Framework 4.x installed.' }
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkDir $_) }
$references += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path (Join-Path $frameworkDir 'WPF') $_) }
$sources = @('PanelApp.cs','TimerEngine.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $panelOutput '紫璃陪伴面板.exe')) $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Panel build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Panel.xaml') -Destination (Join-Path $panelOutput 'Panel.xaml')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\zili.png') -Destination (Join-Path $panelOutput 'assets\zili.png')
Write-Output '紫璃陪伴面板已生成。'
