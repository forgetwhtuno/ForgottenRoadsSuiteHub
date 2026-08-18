$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Csc {
    foreach ($path in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )) {
        if (Test-Path $path) { return $path }
    }
    throw "csc.exe not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

$csc = Find-Csc
$out = Join-Path $env:TEMP "ErenshorSuiteHubDeterministicTests.exe"
$sources = @(
    "src\GameplayReadinessPolicy.cs",
    "src\SuiteUiMetrics.cs",
    "src\SuiteUiGeometry.cs",
    "src\SuiteModuleCatalog.cs",
    "src\ModDiscovery.cs",
    "src\SuiteModuleRegistry.cs",
    "src\SuiteDockPolicy.cs",
    "src\SuiteDockInteractionState.cs",
    "src\SuitePointerOwnershipState.cs",
    "src\SuiteWireCodec.cs",
    "src\SuiteHubView.cs",
    "src\SuiteHubRefreshPolicy.cs",
    "src\SuiteHubPagePolicy.cs",
    "src\SuiteHubLayoutPolicy.cs",
    "src\SuiteHubScrollPolicy.cs",
    "src\SuiteSettingDisplayPolicy.cs",
    "src\SuiteSettingMutationPolicy.cs",
    "src\SuiteQuickClosePolicy.cs",
    "tests\TestAssert.cs",
    "tests\GameplayReadinessPolicyTests.cs",
    "tests\ModDiscoveryTests.cs",
    "tests\SuiteModuleRegistryTests.cs",
    "tests\DiscoverabilityAuditTests.cs",
    "tests\SuiteDockPolicyTests.cs",
    "tests\SuiteDockInteractionStateTests.cs",
    "tests\SuitePointerOwnershipStateTests.cs",
    "tests\SuiteDockSourceContractTests.cs",
    "tests\SuiteWireCodecTests.cs",
    "tests\SuiteUiGeometryTests.cs",
    "tests\SuiteHubLayoutPolicyTests.cs",
    "tests\SuiteHubPagePolicyTests.cs",
    "tests\SuiteHubScrollPolicyTests.cs",
    "tests\SuiteSettingDisplayPolicyTests.cs",
    "tests\SuiteSettingMutationPolicyTests.cs",
    "tests\SuiteHubViewTests.cs",
    "tests\SuiteHubRefreshPolicyTests.cs",
    "tests\SuiteQuickClosePolicyTests.cs",
    "tests\TestRunner.cs"
) | ForEach-Object { Join-Path $ScriptRoot $_ }

& $csc /nologo /target:exe /out:$out $sources
if ($LASTEXITCODE -ne 0) { throw "Suite Hub deterministic tests did not compile." }
$env:ERENSHOR_SUITEHUB_SOURCE_ROOT = $ScriptRoot
& $out
if ($LASTEXITCODE -ne 0) { throw "Suite Hub deterministic tests failed." }

# Release-correctness source guards for the unverified native Escape state in this packet.
$pluginSource = Get-Content (Join-Path $ScriptRoot "src\ErenshorSuiteHubPlugin.cs") -Raw
$nativeEscapeSource = Get-Content (Join-Path $ScriptRoot "src\SuiteNativeEscapeCompatibility.cs") -Raw
$quickSource = Get-Content (Join-Path $ScriptRoot "src\SuiteQuickClosePolicy.cs") -Raw
if (Test-Path (Join-Path $ScriptRoot "src\SuiteFallbackEscapeInput.cs")) { throw "Hub Escape guard failed: non-consuming fallback source still exists." }
if ($pluginSource -match 'Input\.GetKeyDown\s*\(\s*KeyCode\.Escape') { throw "Hub Escape guard failed: direct Escape polling reintroduced." }
if ($nativeEscapeSource -notmatch 'VerifiedDeclaringTypeName\s*=\s*""' -or $nativeEscapeSource -notmatch 'VerifiedMethodName\s*=\s*""') { throw "Hub Escape guard failed: native target changed without updating current-binary evidence/tests." }
if ($quickSource -notmatch 'CloseTopmost' -or $quickSource -notmatch 'ClosedHub\s*\|\|\s*ModuleCloseSuccesses\s*>\s*0') { throw "Hub quick-close guard failed: actual-close/topmost policy missing." }
if ($pluginSource -notmatch 'closePanel returned ok but ui\.state remains open') { throw "Hub quick-close guard failed: module visual closure is not re-verified." }
Write-Host "Suite Hub release Escape/source guards: PASS" -ForegroundColor Green

$dragSource = Get-Content (Join-Path $ScriptRoot "src\SuiteDragGuard.cs") -Raw
$cameraSource = Get-Content (Join-Path $ScriptRoot "src\SuiteCameraUiPatch.cs") -Raw
if ($dragSource -notmatch 'InputButton\.Left' -or $dragSource -notmatch 'Input\.GetMouseButton\(0\)' -or
    $dragSource -notmatch 'OnApplicationFocus' -or $dragSource -notmatch 'OnApplicationPause' -or
    $dragSource -notmatch 'ProcessOwnersKey' -or $dragSource -notmatch 'RestoreProcessBaseline') {
    throw "Suite Hub RC drag guard failed: shared left-only lifecycle/baseline behavior missing."
}
if ($cameraSource -notmatch '\[HarmonyPatch\(typeof\(CameraController\),\s*"UsingUI"\)\]' -or
    $cameraSource -notmatch '\[HarmonyPrepare\]' -or $cameraSource -notmatch 'if\s*\(!__result\s*&&\s*SuiteDragGuard\.HubOwnsDrag\)') {
    throw "Suite Hub camera guard failed: fail-closed monotonic UsingUI postfix missing."
}
foreach ($token in @('UIWindows','activeSelf','ModernControls','releaseMouse','GetAxis','DraggingUIElement')) {
    if ($cameraSource -notmatch [regex]::Escape($token)) { throw "Suite Hub camera guard failed: native proof token missing: $token" }
}
if ($pluginSource -notmatch 'PluginVersion\s*=\s*"0\.5\.3"') { throw "Suite Hub RC version guard failed." }
$settingsSource = Get-Content (Join-Path $ScriptRoot "src\HubSettings.cs") -Raw
if ($settingsSource -notmatch 'public\s+bool\s+UiDiagnostics\s*=\s*false') { throw "Suite Hub RC logging guard failed: UI diagnostics must be opt-in." }
Write-Host "Suite Hub RC camera/gesture source guards: PASS" -ForegroundColor Green

# Launcher disclosure is Image-bar based so the MODS control cannot render a missing TMP glyph.
$hubUiSource = Get-Content (Join-Path $ScriptRoot "src\SuiteHubUi.cs") -Raw
if ($hubUiSource -notmatch 'AddDockChevron\(button\.transform,\s*false\)' -or
    $hubUiSource -notmatch 'SetDockChevron\(true\)' -or
    $hubUiSource -notmatch 'SetDockChevron\(false\)') {
    throw "Suite Hub release polish guard failed: glyph-safe dock chevron state is missing."
}
Write-Host "Suite Hub release polish chevron guard: PASS" -ForegroundColor Green
