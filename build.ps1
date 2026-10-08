# StreamEmber build script for the ScriptHookRDR2DotNet-V2 fork (RDR2ScriptHookRuntime).
# 1) Puts the ScriptHookRDR2 SDK (inc + lib) into sdk\ (from vendor\ or Downloads; never committed).
# 2) Builds ScriptHookRDRDotNet.sln (x64).
# 3) Collects the runtime into builds\<version>\ with the .dll renamed to .asi.
# 4) -Deploy: installs it into the RDR2 folder (-GamePath, or the RDR2_GAME_PATH environment variable).
#
#   .\build.ps1
#   .\build.ps1 -Deploy -GamePath "D:\SteamLibrary\steamapps\common\Red Dead Redemption 2"
[CmdletBinding()]
param(
    [string]$Version = '1.5.5.4',
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    # Explicit SDK folder (contains inc\ and lib\). Default: vendor\ScriptHookRDR2_SDK_*, then ~\Downloads\ScriptHookRDR2_SDK_*
    [string]$SdkPath = '',
    # Install into the RDR2 folder (the one containing RDR2.exe)
    [switch]$Deploy,
    [Alias('GameDir')]
    [string]$GamePath = ''
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# --- SDK -------------------------------------------------------------------------------------------------------
function Find-Sdk {
    if ($SdkPath) { return (Resolve-Path $SdkPath).Path }
    $candidates = @()
    $candidates += Get-ChildItem (Join-Path $PSScriptRoot 'vendor') -Directory -Filter 'ScriptHookRDR2_SDK_*' -ErrorAction SilentlyContinue
    $candidates += Get-ChildItem (Join-Path $env:USERPROFILE 'Downloads') -Directory -Filter 'ScriptHookRDR2_SDK_*' -ErrorAction SilentlyContinue
    $hit = $candidates | Where-Object { Test-Path (Join-Path $_.FullName 'lib\ScriptHookRDR2.lib') } |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return $null
}

$sdkInc = Join-Path $PSScriptRoot 'sdk\inc'
$sdkLib = Join-Path $PSScriptRoot 'sdk\lib\ScriptHookRDR2.lib'
$sdk = Find-Sdk
if ($sdk) {
    New-Item -ItemType Directory -Force -Path $sdkInc, (Split-Path $sdkLib) | Out-Null
    Copy-Item (Join-Path $sdk 'inc\*') $sdkInc -Recurse -Force
    Copy-Item (Join-Path $sdk 'lib\ScriptHookRDR2.lib') $sdkLib -Force
    Write-Host "SDK: $sdk"
} elseif (-not ((Test-Path (Join-Path $sdkInc 'main.h')) -and (Test-Path $sdkLib))) {
    throw 'ScriptHookRDR2 SDK not found. Put it in vendor\ScriptHookRDR2_SDK_<ver>\ (from dev-c.com) or pass -SdkPath.'
}

# --- Build -----------------------------------------------------------------------------------------------------
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found. Install Visual Studio 2022 or later.' }
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild.exe not found.' }
Write-Host "MSBuild: $msbuild"

& $msbuild ScriptHookRDRDotNet.sln -restore -m -nologo -v:minimal "-p:Configuration=$Configuration" '-p:Platform=x64'
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

# --- Collect ---------------------------------------------------------------------------------------------------
$bin = Join-Path $PSScriptRoot "bin\$Configuration"
$out = Join-Path $PSScriptRoot "builds\$Version"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$core = Join-Path $bin 'ScriptHookRDRDotNet.dll'
if (-not (Test-Path $core)) { throw "Missing: $core" }
Copy-Item $core (Join-Path $out 'ScriptHookRDRDotNet.asi') -Force
foreach ($f in @('ScriptHookRDRDotNet.pdb', 'ScriptHookRDRNetAPI.dll', 'ScriptHookRDRNetAPI.pdb', 'ScriptHookRDRNetAPI.xml')) {
    $p = Join-Path $bin $f
    if (Test-Path $p) { Copy-Item $p $out -Force } else { Write-Warning "Missing: $f" }
}
Copy-Item (Join-Path $PSScriptRoot 'ScriptHookRDRDotNet.ini') $out -Force
Write-Host "Done: $out"

# --- Optional install ------------------------------------------------------------------------------------------
if ($GamePath -and -not $Deploy) { $Deploy = [switch]$true }  # old usage: -GameDir <path>
if ($Deploy) {
    $GameDir = if ($GamePath) { $GamePath } else { $env:RDR2_GAME_PATH }
    if (-not $GameDir) { throw 'Game folder unknown: pass -GamePath or set the RDR2_GAME_PATH environment variable.' }
    if (-not (Test-Path (Join-Path $GameDir 'RDR2.exe'))) { throw "RDR2.exe not found in $GameDir" }
    if (Get-Process -Name 'RDR2' -ErrorAction SilentlyContinue) { throw 'RDR2 is running; its files are locked. Close the game.' }
    foreach ($f in @('ScriptHookRDRDotNet.asi', 'ScriptHookRDRNetAPI.dll')) {
        Copy-Item (Join-Path $out $f) $GameDir -Force
    }
    $ini = Join-Path $GameDir 'ScriptHookRDRDotNet.ini'
    if (-not (Test-Path $ini)) { Copy-Item (Join-Path $out 'ScriptHookRDRDotNet.ini') $GameDir }
    New-Item -ItemType Directory -Force -Path (Join-Path $GameDir 'scripts') | Out-Null
    foreach ($need in 'ScriptHookRDR2.dll', 'dinput8.dll') {
        if (-not (Test-Path (Join-Path $GameDir $need))) {
            Write-Warning "$need is missing in the game folder: install ScriptHookRDR2 (dev-c.com) for this game version."
        }
    }
    Write-Host "Installed into $GameDir"
}
