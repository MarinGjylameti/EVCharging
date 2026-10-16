#!/bin/sh
set -eu

project_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
mkdir -p "$project_root/.local/entrypoint-tests"
test_dir=$(mktemp -d "$project_root/.local/entrypoint-tests/run.XXXXXX")
mkdir -p "$test_dir/bin"
cat > "$test_dir/bin/dotnet" <<'STUB'
#!/bin/sh
printf '%s|%s|%s|%s\n' "${DOTNET_ENVIRONMENT:-unset}" "${ASPNETCORE_ENVIRONMENT:-unset}" "$ASPNETCORE_URLS" "$*" >> "$CASE_LOG"
if [ "${FAIL_ACTION:-}" = "$*" ]; then exit 17; fi
STUB
chmod +x "$test_dir/bin/dotnet"

cases=0
run_case() {
  name=$1
  expected_status=$2
  expected_calls=$3
  expected_host=$4
  shift 4
  log="$test_dir/$name.log"
  : > "$log"
  status=0
  env -u DOTNET_ENVIRONMENT -u ASPNETCORE_ENVIRONMENT -u PORT \
    -u EVCHARGING_MIGRATE_ON_START -u EVCHARGING_SEED_DEMO_ON_START \
    -u EVCHARGING_BOOTSTRAP_SUPERADMIN_ON_START \
    -u EVCHARGING_Ocpp__SimulatorEnabled -u EVCHARGING_Features__TestMode -u FAIL_ACTION \
    PATH="$test_dir/bin:$PATH" CASE_LOG="$log" "$@" \
    sh "$project_root/backend/render-entrypoint.sh" > "$test_dir/$name.stdout" 2> "$test_dir/$name.stderr" || status=$?
  test "$status" = "$expected_status" || { printf 'FAIL %s: status %s\n' "$name" "$status"; exit 1; }
  test "$(wc -l < "$log" | tr -d ' ')" = "$expected_calls" || { printf 'FAIL %s: wrong startup calls\n' "$name"; exit 1; }
  if [ "$expected_calls" -gt 0 ]; then
    grep -F -- "$expected_host" "$log" > /dev/null || { printf 'FAIL %s: wrong environment\n' "$name"; exit 1; }
  else
    grep -F 'DOTNET_ENVIRONMENT=Development' "$test_dir/$name.stderr" > /dev/null
  fi
  cases=$((cases + 1))
}

run_case production_default 0 1 'unset|unset|http://0.0.0.0:10000|EVCharging.Api.dll'
run_case migration_only 0 2 'unset|unset|' EVCHARGING_MIGRATE_ON_START=true
run_case bootstrap_only 0 2 'unset|unset|' EVCHARGING_BOOTSTRAP_SUPERADMIN_ON_START=true
run_case migrate_and_bootstrap 0 3 'unset|unset|' EVCHARGING_MIGRATE_ON_START=true EVCHARGING_BOOTSTRAP_SUPERADMIN_ON_START=true
run_case bootstrap_failure 17 1 '--bootstrap-superadmin' EVCHARGING_BOOTSTRAP_SUPERADMIN_ON_START=true FAIL_ACTION='EVCharging.Api.dll --bootstrap-superadmin'
run_case seed_default 0 3 'Development|Development|http://0.0.0.0:10000|' EVCHARGING_MIGRATE_ON_START=true EVCHARGING_Features__TestMode=true EVCHARGING_SEED_DEMO_ON_START=true
run_case test_mode_default 0 1 'Development|Development|' EVCHARGING_Features__TestMode=true
run_case legacy_simulator_ignored 0 1 'unset|unset|' EVCHARGING_Ocpp__SimulatorEnabled=true
run_case legacy_seed_ignored 0 1 'unset|unset|' EVCHARGING_SEED_DEMO_ON_START=true
run_case explicit_flag_off 0 1 'unset|unset|' EVCHARGING_Features__TestMode=false EVCHARGING_SEED_DEMO_ON_START=true EVCHARGING_Ocpp__SimulatorEnabled=true
run_case explicit_dotnet_production 1 0 '' DOTNET_ENVIRONMENT=Production EVCHARGING_MIGRATE_ON_START=true EVCHARGING_Features__TestMode=true EVCHARGING_SEED_DEMO_ON_START=true
run_case explicit_aspnet_production 1 0 '' ASPNETCORE_ENVIRONMENT=Production EVCHARGING_Features__TestMode=true EVCHARGING_SEED_DEMO_ON_START=true
run_case conflicting_environments 1 0 '' DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Development EVCHARGING_Features__TestMode=true EVCHARGING_SEED_DEMO_ON_START=true
run_case dotnet_testing 0 2 'Testing|unset|' DOTNET_ENVIRONMENT=Testing EVCHARGING_Features__TestMode=true EVCHARGING_SEED_DEMO_ON_START=true
run_case aspnet_testing 0 2 'unset|Testing|' ASPNETCORE_ENVIRONMENT=Testing EVCHARGING_Features__TestMode=true EVCHARGING_SEED_DEMO_ON_START=true
run_case production_simulator_explicit 0 1 'Production|unset|' DOTNET_ENVIRONMENT=Production EVCHARGING_Ocpp__SimulatorEnabled=true
run_case custom_port 0 1 'http://0.0.0.0:12345|' PORT=12345
run_case migration_failure 17 1 '--migrate' EVCHARGING_MIGRATE_ON_START=true EVCHARGING_Features__TestMode=true EVCHARGING_SEED_DEMO_ON_START=true FAIL_ACTION='EVCharging.Api.dll --migrate'
run_case seed_failure 17 2 '--seed-demo' EVCHARGING_MIGRATE_ON_START=true EVCHARGING_Features__TestMode=true EVCHARGING_SEED_DEMO_ON_START=true FAIL_ACTION='EVCharging.Api.dll --seed-demo'

# Verify migration -> seed -> server order, and that failures stop the sequence.
sed -n '1p' "$test_dir/seed_default.log" | grep -F -- '--migrate' > /dev/null
sed -n '2p' "$test_dir/seed_default.log" | grep -F -- '--seed-demo' > /dev/null
sed -n '3p' "$test_dir/seed_default.log" | grep -F '|EVCharging.Api.dll' > /dev/null
printf 'Passed %s Render entrypoint cases; no network or database used.\n' "$cases"
