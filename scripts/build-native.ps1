param([string]$Version)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$metadata = Get-Content -LiteralPath (Join-Path $projectRoot 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $Version) { $Version = $metadata.version }
$program = Get-Content -LiteralPath (Join-Path $projectRoot 'native\Program.cs') -Raw -Encoding UTF8
$manifest = Join-Path $projectRoot 'native\app.manifest'
[xml]$manifestXml = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8
if ($Version -ne $metadata.version -or
    $program -notmatch ('public const string Version = "' + [regex]::Escape($Version) + '";') -or
    $program -notmatch ('AssemblyFileVersion\("' + [regex]::Escape($Version) + '\.0"\)') -or
    $manifestXml.assembly.assemblyIdentity.version -ne ($Version + '.0')) {
    throw 'package.json, Program.cs and app.manifest versions must match before building.'
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework C# compiler was not found.' }
$destination = Join-Path $projectRoot 'release'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$executable = Join-Path $destination ('GGD-help-V' + $Version + '-win-x64.exe')
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'native') -Filter '*.cs' -File | Where-Object Name -NE 'ModelTests.cs' | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$executable" "/win32manifest:$manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $destination 'SHA256SUMS.txt'), $hash + '  ' + [System.IO.Path]::GetFileName($executable) + [char]10, [System.Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $executable | Select-Object FullName,Length,@{Name='SHA256';Expression={$hash}}
