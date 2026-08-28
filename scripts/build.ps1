[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$UpdateRepository = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'CmdletForge.slnx'
$arguments = @('build', $solution, '--configuration', $Configuration)
if ($UpdateRepository) {
    $arguments += "-p:UpdateRepository=$UpdateRepository"
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Build mislukt met code $LASTEXITCODE." }

& dotnet run --project (Join-Path $root 'tests\CmdletForge.Tests\CmdletForge.Tests.csproj') --configuration $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests mislukt met code $LASTEXITCODE." }
