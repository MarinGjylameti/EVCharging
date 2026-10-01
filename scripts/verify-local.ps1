$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local/dotnet'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.local/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:NEXT_TELEMETRY_DISABLED = '1'
$env:NEXT_IGNORE_INCORRECT_LOCKFILE = '1'
Push-Location $projectRoot
try {
    dotnet restore backend/EVCharging.slnx --source (Join-Path $env:USERPROFILE '.nuget/packages') --ignore-failed-sources -p:OfflineRestore=true --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed; no download was attempted.' }
    dotnet test backend/EVCharging.slnx --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
    dotnet run --project backend/tools/EVCharging.OcppReplay --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'OCPP replay failed.' }
    Push-Location web
    try {
        npm.cmd run test --offline --cache ../.local/npm --logs-dir ../.local/npm-logs
        if ($LASTEXITCODE -ne 0) { throw 'Web tests failed.' }
        npm.cmd run typecheck --offline --cache ../.local/npm --logs-dir ../.local/npm-logs
        if ($LASTEXITCODE -ne 0) { throw 'Web typecheck failed.' }
        npm.cmd run build --offline --cache ../.local/npm --logs-dir ../.local/npm-logs
        if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' }
    } finally { Pop-Location }
} finally { Pop-Location }
