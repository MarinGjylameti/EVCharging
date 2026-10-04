$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$results = Join-Path $projectRoot ('.local/coverage/backend/' + [Guid]::NewGuid().ToString('N'))
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $projectRoot
try {
    # Dependencies must already be restored. This command never downloads packages.
    dotnet test backend/tests/EVCharging.Tests/EVCharging.Tests.csproj --no-restore --settings backend/tests/coverage.runsettings --collect 'Code Coverage' --results-directory $results --logger 'trx;LogFileName=tests.trx' --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
    $report = Get-ChildItem -LiteralPath $results -Recurse -Filter '*.cobertura.xml' | Select-Object -First 1
    if (!$report) { throw 'Cobertura report was not generated.' }
    [xml]$coverage = Get-Content -LiteralPath $report.FullName -Raw
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $summary = [ordered]@{
        scope = 'Handwritten backend assemblies; generated migrations and obj excluded.'
        linePercent = [math]::Round(100 * [double]::Parse($coverage.coverage.'line-rate', $culture), 2)
        branchPercent = [math]::Round(100 * [double]::Parse($coverage.coverage.'branch-rate', $culture), 2)
        assemblies = @($coverage.coverage.packages.package | ForEach-Object {
            [ordered]@{ name = $_.name; linePercent = [math]::Round(100 * [double]::Parse($_.'line-rate', $culture), 2); branchPercent = [math]::Round(100 * [double]::Parse($_.'branch-rate', $culture), 2) }
        })
        report = $report.FullName
    }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $results 'summary.json') -Encoding utf8
    Write-Output ('Backend coverage: {0}% lines, {1}% branches. Reports: {2}' -f $summary.linePercent, $summary.branchPercent, $results)
} finally { Pop-Location }
