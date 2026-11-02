param([switch]$Build)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable = Join-Path $projectRoot 'release\GGD-help-V0.2.0-win-x64.exe'
if ($Build -or -not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'build-native.ps1')
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'GGD-help executable was not created.' }
Start-Process -FilePath $executable -WorkingDirectory $projectRoot -WindowStyle Hidden | Out-Null
