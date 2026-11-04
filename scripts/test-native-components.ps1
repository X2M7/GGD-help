$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$target = Join-Path $artifacts 'GGD-help-ComponentTests.exe'
$sources = @('RoleCatalog.cs','Models.cs','ThemedControls.cs','PlayerCard.cs','NativeWindows.cs','LauncherForm.cs','SettingsForm.cs','tests\ComponentTests.cs') | ForEach-Object { Join-Path (Join-Path $root 'native') $_ }
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output "/out:$target" /reference:System.Web.Extensions.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'Component test build failed.' }
& $target $artifacts
if ($LASTEXITCODE -ne 0) { throw 'Native component tests failed.' }
