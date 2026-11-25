# deploy.ps1 - copies GameData\Seamless into the game (game must be closed).
#
#   pwsh -File tools\deploy.ps1 [-Ksp <game folder>] [-Dll <built DLL to install instead>]
#
# -Dll: e.g. build\ksp-1.12.5\Seamless.dll (python tools/build.py --ksp <1.12.5 install>
#       --out build\ksp-1.12.5\Seamless.dll) for a KSP 1.12.5 install.
#
# An edited installed settings.cfg is kept; the new defaults go to settings.cfg.new.

param(
    [string]$Ksp = "E:\SteamLibrary\steamapps\common\Kerbal Space Program",
    [string]$Dll = ""
)
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $root "GameData\Seamless"
$dst  = Join-Path $Ksp "GameData\Seamless"

$exe = (Join-Path $Ksp "KSP_x64.exe")
if (Get-Process -Name "KSP_x64" -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -ieq $exe) }) { throw "KSP is running from $Ksp - close it first (loaded DLLs are locked)" }
foreach ($need in @("Plugins\Seamless.dll", "settings.cfg", "Seamless.version")) {
    if (-not (Test-Path (Join-Path $src $need))) { throw "missing build output: $need (run python tools\build.py)" }
}

New-Item -ItemType Directory -Force $dst | Out-Null
$installedCfg = Join-Path $dst "settings.cfg"
$shippedList = Join-Path $PSScriptRoot "shipped-settings.sha256"
$newHash = (Get-FileHash (Join-Path $src "settings.cfg")).Hash
$shipped = @(); if (Test-Path $shippedList) { $shipped = @(Get-Content $shippedList | Where-Object { $_ }) }
if ($shipped -notcontains $newHash) { Add-Content $shippedList $newHash }
$keepCfg = (Test-Path $installedCfg) -and ($shipped -notcontains (Get-FileHash $installedCfg).Hash) -and ((Get-FileHash $installedCfg).Hash -ne $newHash)
if ($keepCfg) { Copy-Item $installedCfg "$installedCfg.keep" -Force }
Copy-Item (Join-Path $src "*") $dst -Recurse -Force
if ($keepCfg) {
    Copy-Item (Join-Path $src "settings.cfg") (Join-Path $dst "settings.cfg.new") -Force
    Move-Item "$installedCfg.keep" $installedCfg -Force
    "kept your edited settings.cfg (new defaults written to settings.cfg.new)"
}

$f = "Plugins\Seamless.dll"
$built = Join-Path $src $f
if ($Dll) {
    $built = (Resolve-Path (Join-Path $root $Dll) -ErrorAction SilentlyContinue)
    if (-not $built) { $built = Resolve-Path $Dll }
    Copy-Item $built (Join-Path $dst $f) -Force
}
$a = (Get-FileHash $built).Hash
$b = (Get-FileHash (Join-Path $dst $f)).Hash
if ($a -ne $b) { throw "hash mismatch after copy: $f" }
"{0}  {1}" -f $b.Substring(0, 16), $f
"DEPLOYED -> $dst"
