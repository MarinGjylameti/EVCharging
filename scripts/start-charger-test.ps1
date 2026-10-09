#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_-]{1,100}$')][string]$ChargePointId = 'NKR2023072805',
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'charger-test-tools.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localRoot = Join-Path $projectRoot '.local'
$toolsRoot = Join-Path $localRoot 'tools'
$runRoot = Join-Path $localRoot ('charger-test/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
$jobs = New-Object 'System.Collections.Generic.List[object]'
$savedEnvironment = @{}
$envNames = @('PATH', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES',
    'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'DOTNET_GENERATE_ASPNET_CERTIFICATE',
    'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE',
    'NEXT_TELEMETRY_DISABLED', 'NEXT_IGNORE_INCORRECT_LOCKFILE', 'EVCHARGING_API_BASE_URL',
    'EVCHARGING_WEB_ORIGIN', 'NODE_ENV', 'npm_config_cache', 'npm_config_logs_dir')
foreach ($name in $envNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$previousTls = [Net.ServicePointManager]::SecurityProtocol
$previousProgress = $ProgressPreference
$failed = $false

function Run-SetupCommand {
    param([string]$Name, [string]$Executable, [string[]]$Arguments, [string]$Directory = $projectRoot)
    Write-Host ("`n" + $Name + '...') -ForegroundColor Cyan
    $job = Start-TestProcess $Name $Executable $Arguments $Directory $runRoot
    $jobs.Add($job)
    while (!$job.Process.WaitForExit(500)) { }
    if ($job.Process.ExitCode -ne 0) {
        Write-Host (Read-TestProcessLog $job)
        throw ($Name + ' deshtoi. Loget: ' + $runRoot)
    }
}

try {
    if ($env:OS -ne 'Windows_NT' -or ![Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64' -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        throw 'Ky launcher kerkon Windows x64.'
    }
    foreach ($path in @($toolsRoot, $runRoot)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
    Assert-PortAvailable 5202
    Assert-PortAvailable 3100
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Write-Host 'EVCharging - prove lokale e UI dhe karikuesit' -ForegroundColor Green
    Write-Host 'Nisja e pare shkarkon SDK/mjete dhe varesite. Nuk nevojitet administrator.'
    Write-Host ('Loget: ' + $runRoot)

    $env:DOTNET_CLI_HOME = Join-Path $localRoot 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $localRoot 'nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    $dotnet = Join-Path $toolsRoot 'dotnet/dotnet.exe'
    if (!(Test-DotnetSdk $dotnet)) {
        $dotnet = Get-NativeTool 'dotnet.exe'
        if (!(Test-DotnetSdk $dotnet)) {
            Write-Host 'Mungon .NET SDK 10. Po merret kopja portative...'
            # Official metadata supplies the current servicing release and its SHA512.
            $manifest = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 60
            $archive = Get-DotnetArchive $manifest
            $zip = Join-Path $toolsRoot 'dotnet-sdk.zip'
            Receive-VerifiedFile $archive $zip
            Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $toolsRoot 'dotnet') -Force
            $dotnet = Join-Path $toolsRoot 'dotnet/dotnet.exe'
            if (!(Test-DotnetSdk $dotnet)) { throw 'SDK e shkarkuar nuk ekzekutohet. Shiko kerkesat e sistemit .NET 10.' }
        }
    }
    $env:DOTNET_ROOT = Split-Path $dotnet
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT

    $node = Join-Path $toolsRoot 'node/node.exe'
    if (!(Test-NodeRuntime $node)) {
        foreach ($folder in @(Get-ChildItem -LiteralPath $toolsRoot -Directory -Filter 'node-v22.*-win-x64' | Sort-Object Name -Descending)) {
            $candidate = Join-Path $folder.FullName 'node.exe'
            if (Test-NodeRuntime $candidate) { $node = $candidate; break }
        }
    }
    if (!(Test-NodeRuntime $node)) { $node = Get-NativeTool 'node.exe' }
    $npmCli = if ($node) { Join-Path (Split-Path $node) 'node_modules/npm/bin/npm-cli.js' } else { '' }
    if (!(Test-NodeRuntime $node) -or !$npmCli -or !(Test-Path -LiteralPath $npmCli -PathType Leaf)) {
        Write-Host 'Po merret Node.js 22 portativ me npm...'
        $checksums = (Invoke-WebRequest -UseBasicParsing -Uri 'https://nodejs.org/download/release/latest-v22.x/SHASUMS256.txt' -TimeoutSec 60).Content
        $archive = Get-NodeArchive $checksums
        $zip = Join-Path $toolsRoot 'node.zip'
        Receive-VerifiedFile $archive $zip
        Expand-Archive -LiteralPath $zip -DestinationPath $toolsRoot -Force
        # Keep the versioned extraction directory; no deletion or global PATH change.
        $node = Join-Path (Join-Path $toolsRoot $archive.Folder) 'node.exe'
        $npmCli = Join-Path (Split-Path $node) 'node_modules/npm/bin/npm-cli.js'
    }
    $cloudflared = Join-Path $toolsRoot 'cloudflared.exe'
    if (!(Test-Path -LiteralPath $cloudflared -PathType Leaf)) { $cloudflared = Get-NativeTool 'cloudflared.exe' }
    if (!$cloudflared) {
        Write-Host 'Po merret cloudflared portativ...'
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/cloudflare/cloudflared/releases/latest' -Headers @{ 'User-Agent' = 'EVCharging-local-test' } -TimeoutSec 60
        $archive = Get-CloudflaredArchive $release
        $cloudflared = Join-Path $toolsRoot 'cloudflared.exe'
        Receive-VerifiedFile $archive $cloudflared
    }
    $env:PATH = (Split-Path $dotnet) + ';' + (Split-Path $node) + ';' + $savedEnvironment['PATH']
    $env:npm_config_cache = Join-Path $localRoot 'npm'
    $env:npm_config_logs_dir = Join-Path $localRoot 'npm-logs'
    $env:NEXT_TELEMETRY_DISABLED = '1'
    $env:NEXT_IGNORE_INCORRECT_LOCKFILE = '1'
    $apiProject = Join-Path $projectRoot 'backend/tools/EVCharging.BrowserTestHost/EVCharging.BrowserTestHost.csproj'
    Run-SetupCommand 'restore' $dotnet @('restore', $apiProject, '--locked-mode', '-p:NuGetAudit=false', '--verbosity', 'minimal')
    Run-SetupCommand 'build' $dotnet @('build', $apiProject, '--no-restore', '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '--verbosity', 'minimal')

    $webRoot = Join-Path $projectRoot 'web'
    $lockHash = (Get-FileHash -LiteralPath (Join-Path $webRoot 'package-lock.json') -Algorithm SHA256).Hash
    $nodeVersion = & $node --version
    $npmStamp = Join-Path $localRoot 'charger-test-npm.txt'
    $wantedStamp = $lockHash + ':' + $nodeVersion
    $installedStamp = if (Test-Path -LiteralPath $npmStamp) { (Get-Content -LiteralPath $npmStamp -Raw).Trim() } else { '' }
    if ($installedStamp -ne $wantedStamp -or !(Test-Path -LiteralPath (Join-Path $webRoot 'node_modules/next/dist/bin/next'))) {
        Run-SetupCommand 'npm-ci' $node @($npmCli, 'ci', '--no-audit', '--no-fund') $webRoot
        Set-Content -LiteralPath $npmStamp -Value $wantedStamp -Encoding UTF8
    }

    Write-Host "`nNisja e backend-it..." -ForegroundColor Cyan
    $apiDll = Join-Path $projectRoot 'backend/tools/EVCharging.BrowserTestHost/bin/Debug/net10.0/EVCharging.BrowserTestHost.dll'
    $backend = Start-TestProcess 'backend' $dotnet @($apiDll, '--local-browser-test') $projectRoot $runRoot
    $jobs.Add($backend)
    Wait-TestHttp 'http://127.0.0.1:5202/health/ready' $backend
    Write-Host 'Nisja e Cloudflare Tunnel...'
    $tunnelConfig = Join-Path $runRoot 'cloudflared.yml'
    Set-Content -LiteralPath $tunnelConfig -Value '{}' -Encoding ASCII
    # The fixture API permits loopback Host headers. Rewrite the tunnel hostname
    # on the local hop for HTTP and WebSocket requests alike.
    $tunnel = Start-TestProcess 'cloudflared' $cloudflared @('tunnel', '--config', $tunnelConfig, '--no-autoupdate', '--protocol', 'http2', '--url', 'http://127.0.0.1:5202', '--http-host-header', '127.0.0.1') $projectRoot $runRoot
    $jobs.Add($tunnel)
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    $publicUrl = $null
    do {
        Assert-TestProcess $backend
        Assert-TestProcess $tunnel
        $tunnelLog = Read-TestProcessLog $tunnel
        $publicUrl = Get-QuickTunnelUrl $tunnelLog
        if ($publicUrl -and $tunnelLog -match 'Registered tunnel connection') { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw ('Cloudflare nuk u lidh. Log: ' + $tunnel.Err) }
        Start-Sleep -Milliseconds 500
    } while ($true)
    Wait-TestHttp ($publicUrl + '/health/ready') $tunnel 90
    Write-Host 'Verifikim OCPP permes linkut publik (BootNotification)...'
    Test-PublicOcpp $publicUrl

    # The web BFF stays on loopback, including SSE; only OCPP uses the public tunnel.
    $env:EVCHARGING_API_BASE_URL = 'http://127.0.0.1:5202'
    $env:EVCHARGING_WEB_ORIGIN = 'http://127.0.0.1:3100'
    $env:NODE_ENV = 'development'
    $frontend = Start-TestProcess 'frontend' $node @((Join-Path $webRoot 'scripts/next-local.cjs'), 'dev', '--hostname', '127.0.0.1', '--port', '3100') $webRoot $runRoot
    $jobs.Add($frontend)
    Wait-TestHttp 'http://127.0.0.1:3100/' $frontend 180

    $wssBase = $publicUrl.Replace('https:', 'wss:') + '/ws/cs'
    $wssUrl = $wssBase + '/' + $ChargePointId
    $report = @"
UI: http://127.0.0.1:3100/
Operator: operator@example.test
Fjalekalimi: Local-test-only-2026!
Driver: driver@example.test
Fjalekalimi: Local-driver-demo-2026!

Linku i PLOTE i karikuesit: $wssUrl
Nese pajisja e shton ID vete, URL baze: $wssBase
Charge Point ID: $ChargePointId
Protokolli: OCPP 1.6 JSON (ocpp1.6)
Kontrolli automatik publik: BootNotification Accepted.

Per mesazhet e pajisjes: UI -> Loget, Charge Point ID $ChargePointId.
Per stacionin fizik: krijo stacion jo-demo, lidhe me ID $ChargePointId te Pajisjet.
URL lokale e shfaqur te stacionet nuk perdoret nga karikuesi; perdor linkun publik me siper.
Loget: $runRoot

Mbaje kompjuterin zgjuar dhe kete dritare hapur gjate proves.
ENTER ose Ctrl+C ndalon backend, frontend dhe tunnel.
Te dhenat e ketij hosti jane ne memorie dhe pastrohen kur ndalohet.
Linku publik ndryshon me cdo nisje te tunnel-it.
"@
    Set-Content -LiteralPath (Join-Path $runRoot 'connection.txt') -Value $report -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $localRoot 'charger-test-latest.txt') -Value $report -Encoding UTF8
    Write-Host ("`n" + $report) -ForegroundColor Green
    try { Set-Clipboard -Value $wssUrl; Write-Host 'Linku i plote WSS u kopjua ne clipboard.' } catch { }
    if (!$NoBrowser) { Start-Process 'http://127.0.0.1:3100/' | Out-Null }
    while ($true) {
        foreach ($job in @($backend, $tunnel, $frontend)) { Assert-TestProcess $job }
        if ([Console]::KeyAvailable -and [Console]::ReadKey($true).Key -eq [ConsoleKey]::Enter) { break }
        Start-Sleep -Milliseconds 500
    }
} catch {
    $failed = $true
    Write-Host ("`nGABIM: " + $_.Exception.Message) -ForegroundColor Red
    Write-Host ('Loget: ' + $runRoot)
    foreach ($job in $jobs) {
        $tail = Read-TestProcessLog $job
        if ($tail) { Write-Host ("`n" + $job.Name + ":`n" + $tail) }
    }
} finally {
    Write-Host "`nNdalimi i proceseve te nisura nga ky launcher..."
    for ($i = $jobs.Count - 1; $i -ge 0; $i--) {
        try { Stop-TestProcess $jobs[$i] } catch { Write-Warning $_.Exception.Message }
    }
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    [Net.ServicePointManager]::SecurityProtocol = $previousTls
    $ProgressPreference = $previousProgress
}
if ($failed) { exit 1 }
