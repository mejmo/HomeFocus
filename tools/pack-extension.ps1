<#
  Packs extension/ into a zip for the Chrome Web Store: the development "key" is removed
  (the store assigns its own ID) and the version is set.
  Usage: tools\pack-extension.ps1 -Version 0.3.0 [-OutDir dist]
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$OutDir = "dist"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path $OutDir "extension"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $root "extension\*") $stage -Recurse

$manifestPath = Join-Path $stage "manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.PSObject.Properties.Remove('key')
$manifest.version = $Version
[IO.File]::WriteAllText((Resolve-Path $manifestPath), ($manifest | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))

$zip = Join-Path $OutDir "HomeFocus-extension-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Output (Resolve-Path $zip).Path
