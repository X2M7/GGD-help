param([string[]]$Fixtures = @())
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$target = Join-Path $artifacts 'GGD-help-ModelTests.exe'
$sources = @('RoleCatalog.cs','Models.cs','StateStore.cs','LegacyStorage.cs','ModelTests.cs') | ForEach-Object { Join-Path (Join-Path $root 'native') $_ }
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output /define:MODEL_TEST_MAIN "/out:$target" /reference:System.Web.Extensions.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'Model test build failed.' }
& $target @Fixtures
if ($LASTEXITCODE -ne 0) { throw 'Native model tests failed.' }
