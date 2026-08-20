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
    "src\ForgottenRoadsDiscoveryCatalog.cs",
    "src\ForgottenRoadsDiscoveryMessage.cs",
    "src\ForgottenRoadsDiscoveryHintPolicy.cs",
    "src\ForgottenRoadsChatStyle.cs",
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
    "tests\ForgottenRoadsDiscoveryHintPolicyTests.cs",
    "tests\ForgottenRoadsDiscoveryMessageTests.cs",
    "tests\ForgottenRoadsChatStyleTests.cs",
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
if ($pluginSource -notmatch 'PluginVersion\s*=\s*"0\.5\.5"') { throw "Suite Hub release version guard failed." }
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

# 11: /frhelp, if implemented, must be a thin dispatch onto the SAME verified composer/discovery
# path as the automatic hint - never a second, independently-maintained text blob - and must reuse
# the already-proven TypeText.CheckCommands interception rather than a new hook.
if ($pluginSource -notmatch '"/frhelp"') { throw "Forgotten Roads discovery guard failed: /frhelp command not registered." }
if ($pluginSource -notmatch 'HandleForgottenRoadsHelpCommand') { throw "Forgotten Roads discovery guard failed: /frhelp has no dedicated handler." }
if ($pluginSource -notmatch 'HandleForgottenRoadsHelpCommand[\s\S]{0,400}ForgottenRoadsDiscoveryMessage\.Compose') {
    throw "Forgotten Roads discovery guard failed: /frhelp does not reuse the verified discovery composer."
}
if (([regex]::Matches($pluginSource, '\[HarmonyPatch\(typeof\(TypeText\),\s*"CheckCommands"\)\]')).Count -ne 1) {
    throw "Forgotten Roads discovery guard failed: /frhelp must reuse the single existing chat-command patch, not add a new one."
}

# The one-time automatic hint must be gated on Hub's own authoritative readiness stage/timing
# policy and must not poll a raw fixed-timestamp/Sleep-style wait.
if ($pluginSource -notmatch '_discoveryHint\.ShouldEmit\(_readiness\.Stage,\s*Time\.unscaledTime\)') {
    throw "Forgotten Roads discovery guard failed: automatic hint is not driven by the readiness stage + unscaledTime."
}
if ($pluginSource -match 'Thread\.Sleep') { throw "Forgotten Roads discovery guard failed: blocking Sleep reintroduced." }

$discoveryMessageSource = Get-Content (Join-Path $ScriptRoot "src\ForgottenRoadsDiscoveryMessage.cs") -Raw
if ($discoveryMessageSource -notmatch 'firstCount') {
    throw "Forgotten Roads discovery guard failed: two-line split logic missing."
}
Write-Host "Forgotten Roads discovery hint source guards: PASS" -ForegroundColor Green

# Native chat color is metadata on ChatLogLine, and the ONLY safe metadata is a hex ColorString
# actually observed on this runtime's native SystemMessages traffic. A named token (the previous
# release shipped one) renders as visible literal markup on the current build's TMP.
if ($pluginSource -match '<color|</color>') { throw "Forgotten Roads discovery guard failed: rich-text markup is embedded in Hub source." }
if ($pluginSource -notmatch 'LogDiscoveryHintLines') { throw "Forgotten Roads discovery guard failed: shared native chat helper missing." }
if (([regex]::Matches($pluginSource, 'new ChatLogLine\(')).Count -ne 1) {
    throw "Forgotten Roads presentation guard failed: there must be exactly one typed ChatLogLine construction site."
}
if ($pluginSource -notmatch 'new ChatLogLine\([\s\S]{0,180}ChatLogLine\.LogType\.SystemMessages[\s\S]{0,60}style') {
    throw "Forgotten Roads presentation guard failed: the emitted line does not carry the observed native style."
}
if ($pluginSource -notmatch 'ForgottenRoadsChatStyle\.CapturedStyle') {
    throw "Forgotten Roads presentation guard failed: emitted style is not sourced from observed native traffic."
}
if ($pluginSource -notmatch 'ForgottenRoadsChatStyle\.SanitizePayload') {
    throw "Forgotten Roads presentation guard failed: visible payload is not sanitized at the emit site."
}
if ($pluginSource -notmatch '\[HarmonyPatch\(typeof\(UpdateSocialLog\),\s*"LogAdd"') {
    throw "Forgotten Roads presentation guard failed: native SystemMessages traffic is not observed."
}
if ($pluginSource -match 'UpdateSocialLog\.LogAdd\(lines\[i\],') {
    throw "Forgotten Roads discovery guard failed: legacy string/color invocation remains in discovery output."
}
$chatStyleSource = Get-Content (Join-Path $ScriptRoot "src\ForgottenRoadsChatStyle.cs") -Raw
if ($chatStyleSource -notmatch 'IsSafeColorString') { throw "Forgotten Roads presentation guard failed: style validation missing." }
if ($chatStyleSource -notmatch 'PlainStyle = ""') { throw "Forgotten Roads presentation guard failed: plain fallback style is not the empty ColorString." }
foreach ($token in @('"cyan"', '"lightblue"', '"white"', '"yellow"', '"grey"', '"red"', '"green"')) {
    if ($pluginSource.Contains($token)) { throw "Forgotten Roads presentation guard failed: named color token hardcoded in plugin source: $token" }
    if ($chatStyleSource.Contains($token)) { throw "Forgotten Roads presentation guard failed: named color token hardcoded in style source: $token" }
}
Write-Host "Forgotten Roads native discovery color/source guards: PASS" -ForegroundColor Green
