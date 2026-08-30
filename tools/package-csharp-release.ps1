[CmdletBinding()]
param(
    [string]$Version = "0.1.0-beta.9"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "versions\csharp-wpf\BalancePet.Wpf.csproj"
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "BalancePet-$Version-win-x64"
$zip = Join-Path $dist "BalancePet-$Version-win-x64.zip"

if (-not (Test-Path -LiteralPath $project)) {
    throw "C# project was not found: $project"
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

dotnet publish $project --configuration Release --runtime win-x64 --self-contained false --output $stage

# Keep the license and attribution next to the executable so binary users see
# the same terms as source users.
Copy-Item (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "LICENSE") $stage
Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $stage
Copy-Item (Join-Path $root "docs\UPGRADE.md") $stage
New-Item -ItemType Directory -Path (Join-Path $stage "docs\licenses") -Force | Out-Null
Copy-Item (Join-Path $root "docs\licenses\MeteorNOX-MIT.txt") (Join-Path $stage "docs\licenses")

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Created $zip"
Write-Host "SHA256 $hash"
