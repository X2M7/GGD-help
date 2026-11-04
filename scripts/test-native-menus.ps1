$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$target = Join-Path $artifacts 'GGD-help-MenuTests.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'native') -Filter '*.cs' -File | Where-Object Name -NE 'ModelTests.cs' | ForEach-Object FullName)
$sources += Join-Path $root 'native\tests\MenuTests.cs'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output /main:GgdHelp.MenuTests "/out:$target" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'Menu test build failed.' }
& $target
if ($LASTEXITCODE -ne 0) { throw 'Native menu regression tests failed.' }
