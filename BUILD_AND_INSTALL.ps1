param(
    [string]$GameDir = "",
    [string]$LunarisLibDir = ""
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Game([string]$Explicit) {
    if ($Explicit -and (Test-Path (Join-Path $Explicit "Erenshor.exe"))) {
        return (Resolve-Path $Explicit).Path
    }

    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} "Steam\steamapps\common\Erenshor" }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles "Steam\steamapps\common\Erenshor" }

    foreach ($root in @((Join-Path ${env:ProgramFiles(x86)} "Steam"), (Join-Path $env:ProgramFiles "Steam"))) {
        if (-not $root) { continue }
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object {
                $library = $_.Groups[1].Value -replace '\\\\','\'
                $candidates += [IO.Path]::Combine($library, "steamapps", "common", "Erenshor")
            }
        }
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path (Join-Path $candidate "Erenshor.exe")) { return (Resolve-Path $candidate).Path }
    }

    throw "Erenshor installation not found. Pass -GameDir 'C:\path\to\Erenshor'."
}

function Find-LunarisLibDir([string]$Explicit, [string]$Game) {
    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }
    $candidates += (Join-Path $ScriptRoot "LunarisLibs")
    $candidates += (Join-Path $Game "plugins")
    $candidates += $Game

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not $candidate) { continue }
        if (Test-Path (Join-Path $candidate "Lunaris.dll")) { return (Resolve-Path $candidate).Path }
    }

    throw "Could not find Lunaris.dll developer reference. Put it in '$ScriptRoot\LunarisLibs' or pass -LunarisLibDir."
}

function Find-HarmonyDll([string]$LunarisLib, [string]$Managed) {
    foreach ($candidate in @((Join-Path $LunarisLib "0Harmony.dll"), (Join-Path $Managed "0Harmony.dll"))) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "Could not find 0Harmony.dll next to Lunaris.dll or in the game's Managed folder."
}

function Find-Csc {
    foreach ($path in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )) {
        if (Test-Path $path) { return $path }
    }
    throw "csc.exe not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

$GameDir = Find-Game $GameDir
$LunarisLibDir = Find-LunarisLibDir $LunarisLibDir $GameDir
$csc = Find-Csc
$managed = Join-Path $GameDir "Erenshor_Data\Managed"
$pluginRoot = Join-Path $GameDir "plugins"
New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null

$refs = @(
    (Join-Path $LunarisLibDir "Lunaris.dll"),
    (Find-HarmonyDll $LunarisLibDir $managed),
    (Join-Path $managed "Assembly-CSharp.dll"),
    (Join-Path $managed "netstandard.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.IMGUIModule.dll"),
    (Join-Path $managed "UnityEngine.TextRenderingModule.dll"),
    (Join-Path $managed "UnityEngine.InputLegacyModule.dll"),
    (Join-Path $managed "UnityEngine.UI.dll"),
    (Join-Path $managed "UnityEngine.UIModule.dll"),
    (Join-Path $managed "Unity.TextMeshPro.dll")
)
foreach ($ref in $refs) { if (-not (Test-Path $ref)) { throw "Missing reference: $ref" } }

$TempDir = Join-Path $env:TEMP ("ErenshorSuiteHub-build-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
$TempDll = Join-Path $TempDir "ErenshorSuiteHub.dll"
$rsp = Join-Path $TempDir "ErenshorSuiteHub.rsp"
$out = Join-Path $pluginRoot "ErenshorSuiteHub.dll"

try {
    $lines = @('/nologo', '/target:library', '/optimize+', ('/out:"{0}"' -f $TempDll))
    $refs | ForEach-Object { $lines += ('/reference:"{0}"' -f $_) }
    Get-ChildItem (Join-Path $ScriptRoot "src") -Filter "*.cs" | Sort-Object Name | ForEach-Object { $lines += '"' + $_.FullName + '"' }
    $lines | Set-Content $rsp -Encoding ASCII

    $lunarisHash = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $LunarisLibDir "Lunaris.dll")).Hash.ToLowerInvariant()
    Write-Host "Building Erenshor Suite Hub as a native Lunaris plugin..." -ForegroundColor Cyan
    Write-Host "  Game:    $GameDir"
    Write-Host "  Lunaris: $LunarisLibDir\Lunaris.dll ($lunarisHash)"
    & $csc "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed. Copy the compiler errors and send them back for correction." }
    if (-not (Test-Path $TempDll)) { throw "Compiler reported success but did not produce $TempDll" }

    Copy-Item -LiteralPath $TempDll -Destination $out -Force
}
finally {
    if (Test-Path $TempDir) { Remove-Item -LiteralPath $TempDir -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "Installed Erenshor Suite Hub to $out" -ForegroundColor Green
Write-Host "Use the draggable MODS UI button in game to open the Overview window." -ForegroundColor Green
