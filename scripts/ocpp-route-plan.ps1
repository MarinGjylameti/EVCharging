#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('direct', 'embedded', 'external')][string]$Mode,
    [string]$TunnelBaseUrl,
    [string]$ApiBaseUrl = 'https://vcharging-api-demo.onrender.com',
    [ValidatePattern('^[A-Za-z0-9_-]{1,40}$')][string]$Identity = 'NKR2023072805'
)

# Read-only plan generator. No downloads, requests, Render edits, or charger commands.
$ErrorActionPreference = 'Stop'
function Get-PublicOrigin([string]$Value, [string]$Label) {
    $parsed = $null
    if (![Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$parsed) -or
        $parsed.Scheme -notin @('https', 'wss') -or $parsed.IsLoopback -or
        $parsed.UserInfo -or $parsed.AbsolutePath -ne '/' -or $parsed.Query -or $parsed.Fragment) {
        throw "$Label duhet te jete https://host ose wss://host, pa ID, kredenciale, path, query apo fragment."
    }
    return $parsed.Authority
}

$apiAuthority = Get-PublicOrigin $ApiBaseUrl 'ApiBaseUrl'
$routeAuthority = $apiAuthority
if ($Mode -ne 'direct') {
    if (!$TunnelBaseUrl) {
        throw 'Merr hostname-in aktual nga loget e connector-it te zgjedhur dhe jep -TunnelBaseUrl https://host.trycloudflare.com.'
    }
    $routeAuthority = Get-PublicOrigin $TunnelBaseUrl 'TunnelBaseUrl'
} elseif ($TunnelBaseUrl) {
    throw 'Prova direct nuk pranon TunnelBaseUrl; duhet te shkoje drejt API-se.'
}

[pscustomobject][ordered]@{
    Mode = $Mode
    BackendEnvironment = "EVCHARGING_Tunnel__Mode=$Mode"
    EmbeddedOrigin = "EVCHARGING_Tunnel__Origin=https://$apiAuthority"
    WebEnvironment = "EVCHARGING_SIMULATOR_WS_BASE_URL=wss://$routeAuthority/ws/cs/"
    HealthUrl = "https://$routeAuthority/health/ready"
    ChargerUrl = "wss://$routeAuthority/ws/cs/$Identity"
    ChargerBaseIfIdIsAppended = "wss://$routeAuthority/ws/cs"
    ProbeCommand = 'Test-PublicOcpp -BaseUrl "https://' + $routeAuthority + '"'
    Note = if ($Mode -eq 'direct') {
        'Pa connector shtese. TestMode nuk ndryshon nga ky plan.'
    } else {
        'Linku ndryshon kur connector-i riniset. TestMode nuk ndryshon nga ky plan.'
    }
}
