param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$DotnetPath = "dotnet"
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$gameRoot = (Resolve-Path -LiteralPath $GameDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $gameRoot "Isolated Inhale.exe")) -or
    !(Test-Path -LiteralPath (Join-Path $gameRoot "BepInEx/core/BepInEx.dll"))) {
    throw "Choose an Isolated Inhale game folder with BepInEx 5 installed."
}
if (Get-Process -Name "Isolated Inhale" -ErrorAction SilentlyContinue) {
    throw "Close the game before updating its plugins."
}
Push-Location $repoRoot
try {
    & $DotnetPath build YourBuddy/YourBuddy.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Build failed; installed plugins were not changed." }
    $buddyDll = Join-Path $repoRoot "YourBuddy/bin/Release/netstandard2.1/YourBuddy.dll"
    $coreDll = Join-Path (Split-Path $repoRoot -Parent) "npc-core-inhl/NPC.Core/bin/Release/netstandard2.1/NPC.Core.dll"
    if (!(Test-Path -LiteralPath $coreDll)) {
        $coreDll = Join-Path $repoRoot "YourBuddy/lib/NPC.Core.dll"
    }
    if (!(Test-Path -LiteralPath $buddyDll) -or !(Test-Path -LiteralPath $coreDll)) {
        throw "Both plugin DLLs are required; installed plugins were not changed."
    }
    $plugins = Join-Path $gameRoot "BepInEx/plugins"
    New-Item -ItemType Directory -Path $plugins -Force | Out-Null
    Copy-Item -LiteralPath $buddyDll -Destination (Join-Path $plugins "YourBuddy.dll") -Force
    Copy-Item -LiteralPath $coreDll -Destination (Join-Path $plugins "NPC.Core.dll") -Force
    Write-Host "Build installed. Launch Isolated Inhale.exe to test."
} finally {
    Pop-Location
}
