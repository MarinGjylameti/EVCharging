// Target the SDK reported by the tester's Expo Go, not an unbounded latest SDK.
const { existsSync, readFileSync, mkdirSync, copyFileSync, readdirSync } = require("node:fs");
const { resolve, join } = require("node:path");
const { spawnSync } = require("node:child_process");

const root = resolve(__dirname, "..");
const local = resolve(root, "../.local");
const temp = join(local, "mobile-build-tmp");
const target = 57;
const args = process.argv.slice(2);
const plan = args.includes("--plan");
const start = args.includes("--start");
if (args.some(arg => !["--plan", "--start"].includes(arg)) || new Set(args).size !== args.length) {
  console.error("Usage: npm run upgrade:expo-go [-- --plan] [--start]");
  process.exit(1);
}

function installedSdk() {
  const file = resolve(root, "node_modules/expo/package.json");
  if (!existsSync(file)) throw new Error("Install the project dependencies with npm install first.");
  return Number(JSON.parse(readFileSync(file, "utf8")).version.split(".")[0]);
}

function run(label, file, ...parameters) {
  console.log(`\n${label}`);
  if (plan) return;
  const result = spawnSync(process.execPath, [file, ...parameters], {
    cwd: root,
    stdio: "inherit",
    env: {
      ...process.env,
      npm_config_cache: join(local, "npm-cache"),
      npm_config_audit: "false",
      npm_config_fund: "false",
      EXPO_NO_TELEMETRY: "1",
      // Keep compiler scratch files inside this project, including on Windows.
      TEMP: temp,
      TMP: temp,
      TMPDIR: temp,
    },
  });
  if (result.error || result.status !== 0) {
    throw new Error(`${label} failed. Stopping here; later steps have not run. ${result.error?.message || "See the command output above."}`);
  }
}

function verify() {
  run("Check Expo dependency compatibility", "node_modules/expo/bin/cli", "install", "--check");
  run("Check native TypeScript", "node_modules/typescript/bin/tsc", "--noEmit");
  const tests = readdirSync(join(root, "tests")).filter(name => name.endsWith(".test.mjs")).sort();
  run("Run mobile regression tests", "--test", ...tests.map(name => join("tests", name)));
}

try {
  const current = installedSdk();
  if (!Number.isInteger(current) || current < 54 || current > target) {
    throw new Error(`This workflow supports SDK 54–${target}; found ${current}. It never downgrades the project.`);
  }
  console.log(`Expo Go compatibility: SDK ${current} → ${target}${plan ? " (plan only; no changes)" : ""}`);
  if (!plan) {
    mkdirSync(temp, { recursive: true });
    const backup = join(local, `mobile-before-sdk57-${new Date().toISOString().replace(/[:.]/g, "-")}`);
    mkdirSync(backup, { recursive: true });
    for (const name of ["package.json", "package-lock.json"]) {
      if (existsSync(join(root, name))) copyFileSync(join(root, name), join(backup, name));
    }
    console.log(`Dependency-file backup: ${backup}`);
  }
  // A previous run can install Expo before plugin registration or devDependencies
  // finish. Repair and validate that SDK before advancing to the next one.
  run(`Finish dependency alignment for installed Expo SDK ${current}`, "node_modules/expo/bin/cli", "install", "--fix");
  verify();
  for (let sdk = current + 1; sdk <= target; sdk++) {
    run(`Upgrade to Expo SDK ${sdk} and align its dependencies`, "node_modules/expo/bin/cli", "install", `expo@^${sdk}.0.0`, "--fix");
    if (!plan && installedSdk() !== sdk) throw new Error(`Expected installed Expo SDK ${sdk}; stopping.`);
    verify();
  }
  run("Export both native platforms", "node_modules/expo/bin/cli", "export", "--platform", "all", "--output-dir", join(local, "mobile-sdk57-export"));
  if (!plan) console.log("\nSDK 57 checks passed. Export is not an APK or a physical-device test.");
  if (start) run("Start Expo Go tunnel after all checks pass", "node_modules/expo/bin/cli", "start", "--go", "--tunnel", "--clear");
  else if (!plan) console.log("Set EXPO_PUBLIC_API_BASE_URL to the reachable API, then run npm run start:tunnel.");
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
