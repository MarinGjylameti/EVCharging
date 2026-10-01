#!/bin/sh
set -eu

export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-10000}"

if [ "${EVCHARGING_MIGRATE_ON_START:-false}" = "true" ]; then
  dotnet EVCharging.Api.dll --migrate
fi

if [ "${EVCHARGING_SEED_DEMO_ON_START:-false}" = "true" ]; then
  dotnet EVCharging.Api.dll --seed-demo
fi

exec dotnet EVCharging.Api.dll
