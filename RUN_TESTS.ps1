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
    "src\SuiteUiGeometry.cs",
    "src\SuiteModuleCatalog.cs",
    "src\ModDiscovery.cs",
    "src\SuiteModuleRegistry.cs",
    "src\SuiteWireCodec.cs",
    "src\SuiteHubView.cs",
    "tests\TestAssert.cs",
    "tests\GameplayReadinessPolicyTests.cs",
    "tests\ModDiscoveryTests.cs",
    "tests\SuiteModuleRegistryTests.cs",
    "tests\SuiteWireCodecTests.cs",
    "tests\SuiteUiGeometryTests.cs",
    "tests\SuiteHubViewTests.cs",
    "tests\TestRunner.cs"
) | ForEach-Object { Join-Path $ScriptRoot $_ }

& $csc /nologo /target:exe /out:$out $sources
if ($LASTEXITCODE -ne 0) { throw "Suite Hub deterministic tests did not compile." }
& $out
if ($LASTEXITCODE -ne 0) { throw "Suite Hub deterministic tests failed." }
