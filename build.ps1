#Requires -Version 5.1
<#
.SYNOPSIS
    StreamEmber Runtime (RDR2): build, package and (optionally) install.

.DESCRIPTION
    1. ScriptHookRDR2 SDK (inc + lib) into sdk\ — never committed, its redistribution is not allowed. Sources, in order:
       -SdkPath, vendor\ScriptHookRDR2_SDK_*, %USERPROFILE%\Downloads\ScriptHookRDR2_SDK_*, then a download from dev-c.com
       (CI). The files are checked against pinned SHA-256 values.
    2. Version: VERSION (major.minor) + commits since it changed = patch (tools/StreamEmber.Build.psm1).
    3. MSBuild ScriptHookRDRDotNet.sln (Release|x64). No .pdb / .xml.
    4. dist\RDR2\ = the game-folder layout:
         StreamEmber.Runtime.RDR2.asi
         StreamEmber\Runtime\StreamEmber.Scripting.RDR2.dll
         StreamEmber\Config\Runtime.ini
         StreamEmber\Licenses\StreamEmber.Runtime.RDR2\LICENSE.txt
         StreamEmber\Manifests\StreamEmber.Runtime.RDR2.json
    5. artifacts\StreamEmber.Runtime.RDR2-<version>.zip (+ .sha256)
    6. -Deploy: copies dist\RDR2 into the game folder (keeps Runtime.ini, disables ScriptHookRDRDotNet.asi).

.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Deploy -GamePath "D:\SteamLibrary\steamapps\common\Red Dead Redemption 2"
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    # Explicit product version (CI passes the computed one); default: computed, with a -dev suffix
    [string]$Version = '',
    # Explicit SDK folder (contains inc\ and lib\)
    [string]$SdkPath = '',
    [switch]$Deploy,
    # RDR2 folder (RDR2.exe). Default: RDR2_GAME_PATH environment variable
    [string]$GamePath = '',
    # Overwrite the game's Runtime.ini with the template
    [switch]$ResetConfig
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Root = $PSScriptRoot
Import-Module (Join-Path $Root 'tools\StreamEmber.Build.psm1') -Force

$Id = 'StreamEmber.Runtime.RDR2'
$Game = 'RDR2'
$Conflicts = @('ScriptHookRDRDotNet.asi')
$Preserve = @('StreamEmber/Config/Runtime.ini')

# --- ScriptHookRDR2 SDK -----------------------------------------------------------------------------------------
$SdkName = 'ScriptHookRDR2_SDK_1.0.1207.73'
# Override (e.g. a private mirror in CI): SCRIPTHOOKRDR2_SDK_URL environment variable / repository variable
$SdkUrl = if ($env:SCRIPTHOOKRDR2_SDK_URL) { $env:SCRIPTHOOKRDR2_SDK_URL } else { "http://www.dev-c.com/files/$SdkName.zip" }
$SdkReferer = 'http://www.dev-c.com/rdr2/scripthookrdr2/'
# The only SDK files the build uses, pinned (from the 1.0.1207.73 SDK)
$SdkHashes = [ordered]@{
    'lib\ScriptHookRDR2.lib' = '1a21c5547e9d0b8accd896c24f5d975150fbf31c9a5e376d75f7fb746fff4e5a'
    'inc\main.h'             = 'c5bc5a0d1368928a009cc7183e1e4006664228e1f4dea0a453360c350504a087'
}

function Test-Sdk([string]$Dir) {
    foreach ($entry in $SdkHashes.GetEnumerator()) {
        $file = Join-Path $Dir $entry.Key
        if (-not (Test-Path $file)) { return $false }
        if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) {
            throw "SDK file does not match the pinned hash: $file"
        }
    }
    return $true
}

function Find-Sdk {
    if ($SdkPath) { return (Resolve-Path $SdkPath).Path }
    $candidates = @()
    $candidates += Get-ChildItem (Join-Path $Root 'vendor') -Directory -Filter 'ScriptHookRDR2_SDK_*' -ErrorAction SilentlyContinue
    if ($env:USERPROFILE) {
        $candidates += Get-ChildItem (Join-Path $env:USERPROFILE 'Downloads') -Directory -Filter 'ScriptHookRDR2_SDK_*' -ErrorAction SilentlyContinue
    }
    $hit = $candidates | Where-Object { Test-Path (Join-Path $_.FullName 'lib\ScriptHookRDR2.lib') } |
        Sort-Object Name -Descending | Select-Object -First 1
    if ($hit) { return $hit.FullName }

    # Download (CI): the SDK may be used for building but not redistributed, so it is fetched at build time
    $target = Join-Path $Root "vendor\$SdkName"
    $zip = Join-Path $Root "vendor\$SdkName.zip"
    New-Item -ItemType Directory -Force -Path (Join-Path $Root 'vendor') | Out-Null
    Write-Host "Downloading $SdkUrl"
    Invoke-WebRequest -Uri $SdkUrl -OutFile $zip -Headers @{ Referer = $SdkReferer } -UseBasicParsing
    $extracted = "$target.extract"
    if (Test-Path $extracted) { Remove-Item $extracted -Recurse -Force }
    Expand-Archive $zip -DestinationPath $extracted -Force
    Remove-Item $zip -Force
    # The archive may contain a top-level folder: move the SDK root (inc\, lib\) to vendor\<SdkName>, so the next run
    # (and the CI cache) finds it directly
    $lib = Get-ChildItem $extracted -Recurse -File -Filter 'ScriptHookRDR2.lib' | Select-Object -First 1
    if (-not $lib) { throw "ScriptHookRDR2.lib not found in the downloaded SDK." }
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Move-Item (Split-Path (Split-Path $lib.FullName)) $target
    if (Test-Path $extracted) { Remove-Item $extracted -Recurse -Force }
    return $target
}

$sdk = Find-Sdk
if (-not (Test-Sdk $sdk)) { throw "Incomplete SDK in $sdk (needs inc\main.h and lib\ScriptHookRDR2.lib)." }
$sdkInc = Join-Path $Root 'sdk\inc'
$sdkLib = Join-Path $Root 'sdk\lib'
New-Item -ItemType Directory -Force -Path $sdkInc, $sdkLib | Out-Null
Copy-Item (Join-Path $sdk 'inc\*') $sdkInc -Recurse -Force
Copy-Item (Join-Path $sdk 'lib\ScriptHookRDR2.lib') $sdkLib -Force
Write-Host "SDK: $sdk"

# --- Build ------------------------------------------------------------------------------------------------------
if (-not $Version) { $Version = Get-SEVersion -RepositoryRoot $Root -Kind Dev }
Write-Host "StreamEmber Runtime (RDR2) $Version" -ForegroundColor Cyan

$msbuild = Find-SEMSBuild
Write-Host "MSBuild: $msbuild"
& $msbuild (Join-Path $Root 'ScriptHookRDRDotNet.sln') -restore -m -nologo -v:minimal `
    "-p:Configuration=$Configuration" '-p:Platform=x64' "-p:SE_VERSION=$Version"
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

# --- Stage (game-folder layout) ---------------------------------------------------------------------------------
$bin = Join-Path $Root "bin\$Configuration"
$stage = Join-Path $Root "dist\$Game"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$runtimeDir = Join-Path $stage 'StreamEmber\Runtime'
$configDir = Join-Path $stage 'StreamEmber\Config'
$licenseDir = Join-Path $stage "StreamEmber\Licenses\$Id"
New-Item -ItemType Directory -Force -Path $runtimeDir, $configDir, $licenseDir | Out-Null

Copy-Item (Join-Path $bin "$Id.asi") $stage
Copy-Item (Join-Path $bin 'StreamEmber.Scripting.RDR2.dll') $runtimeDir
Copy-Item (Join-Path $Root 'package\Config\Runtime.ini') $configDir
Copy-Item (Join-Path $Root 'LICENSE') (Join-Path $licenseDir 'LICENSE.txt')

New-SEManifest -StageDirectory $stage -Id $Id -Name 'StreamEmber Runtime (RDR2)' -Version $Version -Game $Game `
    -Preserve $Preserve -Conflicts $Conflicts -RepositoryRoot $Root `
    -Requires @([ordered]@{ file = 'ScriptHookRDR2.dll'; name = 'Script Hook RDR2 (Alexander Blade)'; url = 'http://www.dev-c.com/rdr2/scripthookrdr2/' },
                [ordered]@{ file = 'dinput8.dll'; name = 'ASI Loader (Script Hook RDR2 package)'; url = 'http://www.dev-c.com/rdr2/scripthookrdr2/' }) | Out-Null

$zip = New-SEPackage -StageDirectory $stage -OutputDirectory (Join-Path $Root 'artifacts') -Id $Id -Version $Version
Write-Host "Package: $zip" -ForegroundColor Green

# --- Install ----------------------------------------------------------------------------------------------------
if ($Deploy) {
    $gameDir = if ($GamePath) { $GamePath } else { $env:RDR2_GAME_PATH }
    if (-not $gameDir) { throw 'Game folder unknown: pass -GamePath or set RDR2_GAME_PATH.' }
    Install-SEPackage -StageDirectory $stage -GameDirectory $gameDir -GameExecutable 'RDR2.exe' -ProcessName 'RDR2' `
        -Preserve $Preserve -Conflicts $Conflicts -ResetConfig:$ResetConfig
    foreach ($need in 'ScriptHookRDR2.dll', 'dinput8.dll') {
        if (-not (Test-Path (Join-Path $gameDir $need))) { Write-Warning "$need missing in the game folder (Script Hook RDR2, dev-c.com)." }
    }
    Write-Host "Installed into $gameDir" -ForegroundColor Green
}
