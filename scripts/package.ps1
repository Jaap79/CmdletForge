[CmdletBinding()]
param(
    [string]$UpdateRepository = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\CmdletForge\CmdletForge.csproj'
$publish = Join-Path $root 'artifacts\publish'
New-Item -ItemType Directory -Force -Path $publish | Out-Null

$arguments = @('publish', $project, '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true', '--output', $publish)
if ($UpdateRepository) {
    $arguments += "-p:UpdateRepository=$UpdateRepository"
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Publish mislukt met code $LASTEXITCODE." }

$source = Join-Path $publish 'CmdletForge.exe'
$target = Join-Path $publish 'CmdletForge-win-x64.exe'
Move-Item -LiteralPath $source -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$target.sha256" -Value "$hash  CmdletForge-win-x64.exe" -Encoding ascii
Get-Item -LiteralPath $target, "$target.sha256"
