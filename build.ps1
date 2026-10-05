param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
New-Item -ItemType Directory -Force "$PSScriptRoot/bin/v$Version" | Out-Null
$sources = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
$assembly = Join-Path $PSScriptRoot 'obj\Version.cs'
New-Item -ItemType Directory -Force "$PSScriptRoot/obj" | Out-Null
Set-Content -LiteralPath $assembly -Value "[assembly: System.Reflection.AssemblyVersion(`"$Version.0`")]`n[assembly: System.Reflection.AssemblyProduct(`"PowerPal`")]`n[assembly: System.Reflection.AssemblyTitle(`"PowerPal`")]"
$output = Join-Path $PSScriptRoot "bin\v$Version\PowerPal.exe"
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$icon = Join-Path $PSScriptRoot 'assets\PowerPal.ico'
$iconArgs = @()
if (Test-Path -LiteralPath $icon) { $iconArgs += "/win32icon:$icon" }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" "/win32manifest:$manifest" @iconArgs /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Windows.Forms.DataVisualization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll @sources $assembly
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Host "Built bin/v$Version/PowerPal.exe"
