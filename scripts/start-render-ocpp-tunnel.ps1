#Requires -Version 5.1
[CmdletBinding()]
param([string]$CloudflaredPath)

# A controlled comparison: change the public entry point, keep the deployed API.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$CloudflaredPath) {
    $CloudflaredPath = Join-Path $projectRoot '.local/tools/cloudflared.exe'
    if (!(Test-Path -LiteralPath $CloudflaredPath -PathType Leaf)) {
        $command = Get-Command 'cloudflared.exe' -CommandType Application -ErrorAction SilentlyContinue
        $CloudflaredPath = if ($command) { $command.Source } else { $null }
    }
}
if (!$CloudflaredPath -or !(Test-Path -LiteralPath $CloudflaredPath -PathType Leaf)) {
    throw 'Mungon cloudflared.exe. Perdore ne kompjuterin e proves se meparshme ose jep -CloudflaredPath me skedarin ekzistues. Ky skript nuk shkarkon apo instalon mjete.'
}
$CloudflaredPath = (Resolve-Path -LiteralPath $CloudflaredPath).Path
$runRoot = Join-Path $projectRoot ('.local/render-tunnel/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$configPath = Join-Path $runRoot 'cloudflared.yml'
Set-Content -LiteralPath $configPath -Value '{}' -Encoding ASCII

Write-Host 'Prove e perkohshme: Cloudflare Tunnel -> API ekzistuese ne Render.' -ForegroundColor Cyan
Write-Host 'Nga adresa HTTPS e afishuar me poshte, nderto:'
Write-Host 'wss://<hostname-i-ri>.trycloudflare.com/ws/cs/NKR2023072805'
Write-Host 'Nese pajisja e shton ID-ne vete, vendos vetem bazen /ws/cs.'
Write-Host 'Mbaje terminalin hapur gjate proves; Ctrl+C e ndalon tunelin.'
Write-Host 'Ky eshte test krahasimi, jo adrese e perhershme per prodhim.'

# Match Render's virtual host and certificate name; keep origin TLS verification.
# Explicit booleans also override inherited cloudflared environment defaults.
& $CloudflaredPath tunnel `
    --config $configPath `
    --no-autoupdate `
    --protocol http2 `
    --url https://vcharging-api-demo.onrender.com `
    --http-host-header vcharging-api-demo.onrender.com `
    --origin-server-name vcharging-api-demo.onrender.com `
    --no-tls-verify=false `
    --http2-origin=false
if ($LASTEXITCODE -ne 0) { throw ('Cloudflare Tunnel perfundoi me kodin ' + $LASTEXITCODE) }
