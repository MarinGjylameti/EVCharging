#Requires -Version 5.1
# Offline regression checks. No downloads, restore, package installation or tunnel creation.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'charger-test-tools.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $projectRoot ('.local/charger-launcher-tests/' + [Guid]::NewGuid().ToString('N') + ' path with spaces')
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$checks = 0
$jobs = New-Object 'System.Collections.Generic.List[object]'
function Check {
    param([bool]$Condition, [string]$Description)
    if (!$Condition) { throw ('FAIL: ' + $Description) }
    $script:checks++
}
function Check-Throws {
    param([scriptblock]$Action, [string]$Description)
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    Check $threw $Description
}
try {
    foreach ($file in @('charger-test-tools.ps1', 'start-charger-test.ps1', 'test-charger-test.ps1')) {
        $tokens = $null; $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file), [ref]$tokens, [ref]$parseErrors)
        Check (@($parseErrors).Count -eq 0) ('PowerShell syntax: ' + $file)
    }

    $sdkHash = 'a' * 128
    $sdkFile = @{ name = 'dotnet-sdk-win-x64.zip'; rid = 'win-x64'; hash = $sdkHash; url = 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/sdk.zip' }
    $manifest = @{ 'latest-sdk' = '10.0.401'; releases = @(@{ sdk = @{ version = '10.0.401'; files = @($sdkFile) } }) }
    Check ((Get-DotnetArchive $manifest).Hash -eq $sdkHash) 'SDK manifest selection'
    $manifest.releases[0] = @{ sdk = @{ version = '10.0.400' }; sdks = @(@{ version = '10.0.401'; files = @($sdkFile) }) }
    Check ((Get-DotnetArchive $manifest).Hash -eq $sdkHash) 'SDK servicing release in sdks array'
    $sdkFile.hash = 'bad'
    Check-Throws { Get-DotnetArchive $manifest } 'Reject missing/corrupt SDK hash'

    $nodeHash = 'b' * 64
    $nodeArchive = Get-NodeArchive ($nodeHash + "  node-v22.23.3-win-x64.zip`r`n" + ('c' * 64) + '  node-v22.23.3-win-arm64.zip')
    Check ($nodeArchive.Hash -eq $nodeHash -and $nodeArchive.Url -eq 'https://nodejs.org/download/release/v22.23.3/node-v22.23.3-win-x64.zip') 'Node versioned URL and architecture'
    Check-Throws { Get-NodeArchive 'bad node-v22.23.3-win-x64.zip' } 'Reject missing Node hash'
    $asset = @{ name = 'cloudflared-windows-amd64.exe'; digest = 'sha256:' + $nodeHash; browser_download_url = 'https://github.com/cloudflare/cloudflared/releases/download/test/cloudflared-windows-amd64.exe' }
    $release = @{ assets = @($asset); body = '' }
    Check ((Get-CloudflaredArchive $release).Hash -eq $nodeHash) 'Cloudflared asset digest'
    $asset.digest = $null
    $release.body = 'cloudflared-windows-amd64.exe: ' + $nodeHash
    Check ((Get-CloudflaredArchive $release).Hash -eq $nodeHash) 'Cloudflared release checksum fallback'
    $release.body = ''
    Check-Throws { Get-CloudflaredArchive $release } 'Reject unsigned checksum omission'

    $fakeDotnet = Join-Path $testRoot 'runtime-only.cmd'
    Set-Content -LiteralPath $fakeDotnet -Encoding ASCII -Value @'
@echo off
if "%1"=="--list-runtimes" echo Microsoft.AspNetCore.App 10.0.12 [runtime]
exit /b 0
'@
    Check (!(Test-DotnetSdk $fakeDotnet)) 'Runtime-only dotnet is insufficient'
    Set-Content -LiteralPath $fakeDotnet -Encoding ASCII -Value @'
@echo off
if "%1"=="--list-sdks" echo 10.0.401 [sdk]
if "%1"=="--list-runtimes" echo Microsoft.AspNetCore.App 10.0.12 [runtime]
exit /b 0
'@
    Check (Test-DotnetSdk $fakeDotnet) 'SDK with ASP.NET runtime accepted'
    Set-Content -LiteralPath $fakeDotnet -Encoding ASCII -Value @'
@echo off
if "%1"=="--list-sdks" echo 10.0.401 [sdk]
exit /b 0
'@
    Check (!(Test-DotnetSdk $fakeDotnet)) 'Missing ASP.NET runtime detected'

    $fixture = Join-Path $testRoot 'download-fixture.bin'
    $destination = Join-Path $testRoot 'verified.bin'
    Set-Content -LiteralPath $fixture -Encoding ASCII -Value 'verified archive contents'
    Set-Content -LiteralPath $destination -Encoding ASCII -Value 'existing tool'
    # Substitute the downloader, so checksum verification is exercised with no network.
    function Invoke-WebRequest {
        param([switch]$UseBasicParsing, [string]$Uri, [string]$OutFile, [int]$TimeoutSec)
        Copy-Item -LiteralPath $fixture -Destination $OutFile -Force
    }
    $archive = @{ Url = 'offline-fixture'; Hash = '0' * 64; Algorithm = 'SHA256' }
    Check-Throws { Receive-VerifiedFile $archive $destination } 'Checksum mismatch stops installation'
    Check ((Get-Content -LiteralPath $destination -Raw).Trim() -eq 'existing tool') 'Checksum mismatch preserves existing tool'
    $archive.Hash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    Receive-VerifiedFile $archive $destination
    Check ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $archive.Hash) 'Verified download promoted'
    Remove-Item Function:\Invoke-WebRequest

    Check ((Get-QuickTunnelUrl "INF | https://some-test-name.trycloudflare.com |`nINF Registered tunnel connection") -eq 'https://some-test-name.trycloudflare.com') 'Extract public tunnel URL'
    Check ($null -eq (Get-QuickTunnelUrl 'https://test.trycloudflare.com.evil.example')) 'Reject hostname suffix'
    Check ($null -eq (Get-QuickTunnelUrl 'https://developers.cloudflare.com/tunnel')) 'No URL until tunnel logs publish one'

    $powershell = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $childScript = Join-Path $testRoot 'argument child.ps1'
    Set-Content -LiteralPath $childScript -Encoding ASCII -Value '[Environment]::GetCommandLineArgs() | ConvertTo-Json -Compress'
    $testArgs = @('', 'with spaces', 'quote"inside', 'C:\folder with spaces\', 'slashes\\"quote', 'dollar$(literal)&value')
    $job = Start-TestProcess 'arguments' $powershell (@('-NoProfile', '-File', $childScript) + $testArgs) $testRoot $testRoot
    $jobs.Add($job)
    Check ($job.Process.WaitForExit(15000) -and $job.Process.ExitCode -eq 0) 'Argument child completes'
    $actualArgs = Get-Content -LiteralPath $job.Out -Raw | ConvertFrom-Json
    $actualArgs = @($actualArgs | Select-Object -Last $testArgs.Count)
    for ($i = 0; $i -lt $testArgs.Count; $i++) { Check ($actualArgs[$i] -ceq $testArgs[$i]) ('Argument roundtrip ' + $i) }
    $failureScript = Join-Path $testRoot 'failed child.ps1'
    Set-Content -LiteralPath $failureScript -Encoding ASCII -Value 'exit 7'
    $failedJob = Start-TestProcess 'failure' $powershell @('-NoProfile', '-File', $failureScript) $testRoot $testRoot
    $jobs.Add($failedJob)
    Check ($failedJob.Process.WaitForExit(15000) -and $failedJob.Process.ExitCode -eq 7) 'Failure exit code preserved on PowerShell 5.1'
    Check-Throws { Wait-TestHttp 'http://127.0.0.1:1/' $failedJob 30 } 'Readiness fails immediately for a dead service'

    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { Check-Throws { Assert-PortAvailable $listener.LocalEndpoint.Port } 'Occupied port detected without killing its owner' }
    finally { $listener.Stop() }

    $sleepScript = Join-Path $testRoot 'sleep child.ps1'
    Set-Content -LiteralPath $sleepScript -Encoding ASCII -Value 'Start-Sleep -Seconds 60'
    $sibling = Start-TestProcess 'sibling' $powershell @('-NoProfile', '-File', $sleepScript) $testRoot $testRoot
    $jobs.Add($sibling)
    $parentScript = Join-Path $testRoot 'parent.ps1'
    Set-Content -LiteralPath $parentScript -Encoding ASCII -Value @'
param([string]$ChildScript)
$child = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList ('-NoProfile -File "' + $ChildScript + '"') -WindowStyle Hidden -PassThru
$child.Id
Start-Sleep -Seconds 60
'@
    $parent = Start-TestProcess 'parent' $powershell @('-NoProfile', '-File', $parentScript, $sleepScript) $testRoot $testRoot
    $jobs.Add($parent)
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        Assert-TestProcess $parent
        $childPidText = Get-Content -LiteralPath $parent.Out -Raw
        if ($childPidText) { $childPidText = $childPidText.Trim() }
    } while (!$childPidText -and [DateTime]::UtcNow -lt $deadline)
    Check ([bool]$childPidText) 'Parent spawned child'
    $grandchild = Get-Process -Id ([int]$childPidText)
    Stop-TestProcess $parent
    Check ($parent.Process.WaitForExit(5000)) 'Owned parent stopped'
    Check ($grandchild.WaitForExit(5000)) 'Owned descendant stopped'
    Assert-TestProcess $sibling
    Check (!$sibling.Process.HasExited) 'Unrelated sibling stays alive'
    Check-Throws { Assert-TestProcess $parent } 'Unexpected service exit detected'
    Write-Host ("PASS: $checks offline launcher checks (Windows PowerShell " + $PSVersionTable.PSVersion + ').')
} finally {
    for ($i = $jobs.Count - 1; $i -ge 0; $i--) { Stop-TestProcess $jobs[$i] }
}
