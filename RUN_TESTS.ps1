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

# Pure file-presence discovery logic used by the Overview tab - no UnityEngine or game assembly
# dependency, so this stays testable outside the game. See src/ModDiscovery.cs.
$out = Join-Path $env:TEMP "ErenshorSuiteHubModDiscoveryTests.exe"
& $csc /nologo /target:exe /out:$out `
    (Join-Path $ScriptRoot "src\ModDiscovery.cs") `
    (Join-Path $ScriptRoot "tests\ModDiscoveryTests.cs") `
    (Join-Path $ScriptRoot "tests\TestRunner.cs")
if ($LASTEXITCODE -ne 0) { throw "Suite Hub mod-discovery tests did not compile." }
& $out
if ($LASTEXITCODE -ne 0) { throw "Suite Hub mod-discovery tests failed." }
