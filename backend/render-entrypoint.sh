#!/bin/sh
set -eu

export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-10000}"

# These flags explicitly select a demo. Keep an explicitly configured host
# environment authoritative, including DOTNET_ENVIRONMENT's precedence.
if [ "${EVCHARGING_SEED_DEMO_ON_START:-false}" = "true" ] ||
   [ "${EVCHARGING_Ocpp__SimulatorEnabled:-false}" = "true" ]; then
  if [ -z "${DOTNET_ENVIRONMENT:-}" ] && [ -z "${ASPNETCORE_ENVIRONMENT:-}" ]; then
    export DOTNET_ENVIRONMENT=Development
    export ASPNETCORE_ENVIRONMENT=Development
    printf '%s\n' 'Demo startup: using Development because no host environment was configured.'
  fi
fi

if [ "${EVCHARGING_SEED_DEMO_ON_START:-false}" = "true" ]; then
  startup_environment=$(printf '%s' "${DOTNET_ENVIRONMENT:-${ASPNETCORE_ENVIRONMENT:-Production}}" | tr '[:upper:]' '[:lower:]')
  case "$startup_environment" in
    development|testing) ;;
    *)
      printf '%s\n' 'Demo seed requires Development or Testing. For this demo set DOTNET_ENVIRONMENT=Development and ASPNETCORE_ENVIRONMENT=Development. For production disable EVCHARGING_SEED_DEMO_ON_START.' >&2
      exit 1
      ;;
  esac
fi

if [ "${EVCHARGING_MIGRATE_ON_START:-false}" = "true" ]; then
  dotnet EVCharging.Api.dll --migrate
fi

if [ "${EVCHARGING_BOOTSTRAP_SUPERADMIN_ON_START:-false}" = "true" ]; then
  dotnet EVCharging.Api.dll --bootstrap-superadmin
fi

if [ "${EVCHARGING_SEED_DEMO_ON_START:-false}" = "true" ]; then
  dotnet EVCharging.Api.dll --seed-demo
fi

exec dotnet EVCharging.Api.dll
