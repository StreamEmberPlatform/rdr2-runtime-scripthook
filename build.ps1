#Requires -Version 5.1
<#
.SYNOPSIS
    StreamEmber Runtime (RDR2): build, package and (optionally) install.

.DESCRIPTION
    1. ScriptHookRDR2 SDK: sdk\inc\main.h + sdk\lib\ScriptHookRDR2.lib, kept in the repository (sdk\README.md).
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
