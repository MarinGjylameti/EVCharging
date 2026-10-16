#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$planner = Join-Path $PSScriptRoot 'ocpp-route-plan.ps1'
$cases = 0
function Assert-Equal($Actual, $Expected) {
    if ($Actual -ne $Expected) { throw "Expected '$Expected', got '$Actual'." }
    $script:cases++
}
function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Invalid route was accepted.' }
    $script:cases++
}

$direct = & $planner -Mode direct
Assert-Equal $direct.ChargerUrl 'wss://vcharging-api-demo.onrender.com/ws/cs/NKR2023072805'
Assert-Equal $direct.WebEnvironment 'EVCHARGING_SIMULATOR_WS_BASE_URL=wss://vcharging-api-demo.onrender.com/ws/cs/'
Assert-Equal $direct.HealthUrl 'https://vcharging-api-demo.onrender.com/health/ready'
Assert-Equal $direct.ProbeCommand 'Test-PublicOcpp -BaseUrl "https://vcharging-api-demo.onrender.com"'
foreach ($mode in @('embedded', 'external')) {
    $plan = & $planner -Mode $mode -TunnelBaseUrl 'https://example-current.trycloudflare.com/' -Identity 'SIM-007'
    Assert-Equal $plan.BackendEnvironment "EVCHARGING_Tunnel__Mode=$mode"
    Assert-Equal $plan.ChargerUrl 'wss://example-current.trycloudflare.com/ws/cs/SIM-007'
    Assert-Equal $plan.ChargerBaseIfIdIsAppended 'wss://example-current.trycloudflare.com/ws/cs'
    Assert-Equal $plan.EmbeddedOrigin 'EVCHARGING_Tunnel__Origin=https://vcharging-api-demo.onrender.com'
    Assert-Rejected { & $planner -Mode $mode }
}
$secure = & $planner -Mode external -TunnelBaseUrl 'wss://trial.example.test:443'
Assert-Equal $secure.ChargerUrl 'wss://trial.example.test/ws/cs/NKR2023072805'
foreach ($invalid in @('http://example.test', 'ws://example.test', 'https://localhost',
    'https://127.0.0.1', 'https://user:password@example.test', 'https://example.test/ws/cs/SIM-007',
    'https://example.test/?token=secret', 'https://example.test/#fragment', 'not-a-url')) {
    Assert-Rejected { & $planner -Mode embedded -TunnelBaseUrl $invalid }
}
Assert-Rejected { & $planner -Mode direct -TunnelBaseUrl 'https://example.test' }
Assert-Rejected { & $planner -Mode invalid }
Assert-Rejected { & $planner -Mode direct -Identity 'bad/id' }
Assert-Rejected { & $planner -Mode direct -ApiBaseUrl 'https://api.example.test/path' }
Write-Host "Passed $cases route-plan assertions; no network, service or charger changes."
