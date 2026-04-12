$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

param(
    [string] $Configuration = "Debug",
    [string] $PluginsRoot = $env:BTCPAY_PLUGIN_DIR,
    [string] $SettingsConfig = $env:BTCPAY_SETTINGS_CONFIG
)

$pluginId = "BTCPayServer.Plugins.Payjoin"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$sourceDir = Join-Path $repoRoot "$pluginId/bin/$Configuration/net8.0"
$isWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$isMacPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)
$isLinuxPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)

function Fail {
    param([string] $Message)
    throw $Message
}

function Get-PluginsRootFromSettings {
    param([string] $ConfigPath)

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        Fail "settings.config not found: $ConfigPath"
    }

    foreach ($line in Get-Content -LiteralPath $ConfigPath) {
        if ($line -match '^\s*#') {
            continue
        }

        $match = [regex]::Match($line, '^\s*plugindir\s*=\s*(.+?)\s*$')
        if ($match.Success) {
            return $match.Groups[1].Value
        }
    }

    return $null
}

function Get-DefaultPluginsRoot {
    if ($isWindowsPlatform) {
        if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
            Fail "APPDATA is not set"
        }

        return Join-Path $env:APPDATA "BTCPayServer/Plugins"
    }

    if ([string]::IsNullOrWhiteSpace($HOME)) {
        Fail "HOME is not set"
    }

    return Join-Path $HOME ".btcpayserver/Plugins"
}

if ($isWindowsPlatform) {
    $libName = "payjoin_ffi.dll"
} elseif ($isMacPlatform) {
    $libName = "libpayjoin_ffi.dylib"
} elseif ($isLinuxPlatform) {
    $libName = "libpayjoin_ffi.so"
} else {
    Fail "Unsupported OS"
}

if ([string]::IsNullOrWhiteSpace($PluginsRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($SettingsConfig)) {
        $PluginsRoot = Get-PluginsRootFromSettings $SettingsConfig
        if ([string]::IsNullOrWhiteSpace($PluginsRoot)) {
            $PluginsRoot = Get-DefaultPluginsRoot
        }
    } else {
        $PluginsRoot = Get-DefaultPluginsRoot
    }
}

$pluginsRootResolved = [System.IO.Path]::GetFullPath($PluginsRoot)
$targetPluginDir = Join-Path $pluginsRootResolved $pluginId

if (-not (Test-Path -LiteralPath $sourceDir)) {
    Fail "Build output directory not found: $sourceDir"
}

$requiredFiles = @(
    "$pluginId.dll",
    "$pluginId.deps.json",
    $libName
)

$optionalFiles = @(
    "$pluginId.pdb"
)

foreach ($file in $requiredFiles) {
    $sourceFile = Join-Path $sourceDir $file
    if (-not (Test-Path -LiteralPath $sourceFile)) {
        Fail "Required build artifact is missing: $sourceFile"
    }
}

if (Test-Path -LiteralPath $targetPluginDir) {
    Remove-Item -LiteralPath $targetPluginDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $targetPluginDir | Out-Null

foreach ($file in $requiredFiles) {
    Copy-Item -LiteralPath (Join-Path $sourceDir $file) -Destination (Join-Path $targetPluginDir $file)
}

foreach ($file in $optionalFiles) {
    $sourceFile = Join-Path $sourceDir $file
    if (Test-Path -LiteralPath $sourceFile) {
        Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $targetPluginDir $file)
    }
}

Write-Host "Staged $pluginId"
Write-Host "  Configuration: $Configuration"
Write-Host "  Source: $sourceDir"
Write-Host "  Plugins root: $pluginsRootResolved"
Write-Host "  Target: $targetPluginDir"
Write-Host "  Files:"

foreach ($file in @($requiredFiles + $optionalFiles)) {
    $targetFile = Join-Path $targetPluginDir $file
    if (Test-Path -LiteralPath $targetFile) {
        Write-Host "    - $file"
    }
}

$dllHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $targetPluginDir "$pluginId.dll")).Hash.ToLowerInvariant()
$libHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $targetPluginDir $libName)).Hash.ToLowerInvariant()

Write-Host "  Hashes:"
Write-Host "    - $pluginId.dll: $dllHash"
Write-Host "    - $libName: $libHash"
Write-Host "Restart BTCPay to load the staged plugin."
