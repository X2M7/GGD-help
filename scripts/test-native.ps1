param(
    [string]$Executable,
    [switch]$CompileOnly
)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
if (-not $Executable) {
    $Executable = Join-Path $projectDirectory 'release\GGD-help-V0.2.0-win-x64.exe'
}
$artifactDirectory = Join-Path $projectDirectory 'artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$driver = Join-Path $artifactDirectory 'GGD-help-NativeSmoke.exe'
$source = Join-Path $projectDirectory 'native\tests\NativeSmoke.cs'
$references = @(
    'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll',
    (Join-Path $wpf 'UIAutomationClient.dll'),
    (Join-Path $wpf 'UIAutomationTypes.dll'),
    (Join-Path $wpf 'WindowsBase.dll')
)
$compilerArguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', "/out:$driver")
$compilerArguments += $references | ForEach-Object { "/reference:$_" }
$compilerArguments += $source
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "Native smoke driver compilation failed ($LASTEXITCODE)." }
if ($CompileOnly) {
    Write-Output "Compiled native smoke driver: $driver"
    exit 0
}
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
& $driver $resolvedExecutable $artifactDirectory
if ($LASTEXITCODE -ne 0) { throw "Native UI smoke failed ($LASTEXITCODE). See artifacts and the printed isolated profile path." }
