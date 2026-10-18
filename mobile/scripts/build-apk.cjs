const { existsSync, readFileSync, writeFileSync, mkdirSync, readdirSync } = require("node:fs");
const { resolve, join } = require("node:path");
const { spawnSync } = require("node:child_process");
const { linkedProjectId } = require("../config/project-link.cjs");
const { buildEnvironment } = require("../config/build-environment.cjs");

const root = resolve(__dirname, "..");
const local = resolve(root, "../.local");
const tools = join(local, "mobile-tools");
const temp = join(local, "mobile-build-tmp");
const eas = join(tools, "node_modules/eas-cli/bin/run");
const profile = "preview-ui";
const args = process.argv.slice(2);
if (args.length) {
  if (args.length !== 1 || args[0] !== "--plan") {
    console.error("Usage: npm run build:apk [-- --plan]");
    process.exit(1);
  }
  console.log("Check SDK 57, TypeScript and mobile tests → install EAS CLI locally if needed → create/link the EVCharging Expo project → validate the UI-preview configuration → export Android → run the signed internal APK build. No store submission, commit or push. EAS supplies the real download link after success.");
  process.exit(0);
}

function run(label, file, parameters, { cwd = root, env = {} } = {}) {
  console.log(`\n${label}`);
  const result = spawnSync(process.execPath, [file, ...parameters], {
    cwd, stdio: "inherit",
    env: { ...process.env, EXPO_NO_TELEMETRY: "1",
      npm_config_cache: join(local, "npm-cache"), npm_config_audit: "false", npm_config_fund: "false",
      TEMP: temp, TMP: temp, TMPDIR: temp, ...env },
  });
  if (result.error || result.status !== 0) throw new Error(`${label} failed. Stopping; no APK is being claimed. ${result.error?.message || "See the output above."}`);
}

try {
  const expo = JSON.parse(readFileSync(join(root, "node_modules/expo/package.json"), "utf8"));
  if (!expo.version.startsWith("57.")) throw new Error("Finish npm run upgrade:expo-go before building this APK.");
  mkdirSync(temp, { recursive: true });
  run("Check TypeScript", join(root, "node_modules/typescript/bin/tsc"), ["--noEmit"]);
  run("Run mobile tests", "--test", readdirSync(join(root, "tests")).filter(name => name.endsWith(".test.mjs")).sort().map(name => join(root, "tests", name)));
  if (!existsSync(eas)) {
    if (!process.env.npm_execpath) throw new Error("Run this workflow with npm run build:apk.");
    run("Install EAS CLI inside the project", process.env.npm_execpath, ["install", "--prefix", tools, "--no-audit", "--no-fund", "eas-cli"]);
  }

  let id = linkedProjectId();
  if (!id) {
    // EAS can write a static app.json automatically. This initialization-only
    // folder avoids rewriting or temporarily removing the real dynamic config.
    const init = join(local, "evcharging-eas-init");
    const appFile = join(init, "app.json");
    mkdirSync(init, { recursive: true });
    if (!existsSync(appFile)) writeFileSync(appFile, JSON.stringify({ expo: { name: "EVCharging", slug: "evcharging", version: "0.1.0", sdkVersion: "57.0.0" } }, null, 2) + "\n");
    const app = JSON.parse(readFileSync(appFile, "utf8"));
    if (app.expo?.slug !== "evcharging") throw new Error("The initialization folder belongs to a different Expo project.");
    if (!existsSync(join(init, "package.json"))) writeFileSync(join(init, "package.json"), JSON.stringify({ name: "evcharging-project-init", private: true }, null, 2) + "\n");
    const requestedId = process.env.EXPO_PUBLIC_EAS_PROJECT_ID?.trim();
    if (requestedId && !/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(requestedId)) throw new Error("EXPO_PUBLIC_EAS_PROJECT_ID must be a valid project UUID.");
    if (requestedId && app.expo?.extra?.eas?.projectId && requestedId !== app.expo.extra.eas.projectId) throw new Error("The saved initialization project differs from EXPO_PUBLIC_EAS_PROJECT_ID.");
    run("Create or link the EVCharging Expo project", eas, ["project:init", "--force", ...(requestedId ? ["--id", requestedId] : [])], { cwd: init });
    const initialized = JSON.parse(readFileSync(appFile, "utf8")).expo;
    if (initialized?.slug !== "evcharging") throw new Error("EAS linked a different project slug. No build was started.");
    id = initialized?.extra?.eas?.projectId;
    if (typeof id !== "string" || !/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(id)) throw new Error("EAS did not save a valid project ID. No build was started.");
    // The project ID is public build metadata, never an authentication token.
    writeFileSync(join(root, "config/eas-project.json"), JSON.stringify({ projectId: id }, null, 2) + "\n", { flag: "wx" });
  }

  const settings = JSON.parse(readFileSync(join(root, "eas.json"), "utf8"));
  const env = { ...settings.build[profile].env, EAS_BUILD_PROFILE: profile };
  buildEnvironment({ ...process.env, ...env }, id);
  run("Validate the Android UI preview", join(__dirname, "check-release.cjs"), ["android"], { env });
  run("Export the Android UI preview", join(root, "node_modules/expo/bin/cli"), ["export", "--platform", "android", "--output-dir", join(local, "mobile-apk-export")], { env });
  console.log("\nThis internal APK uses the demo API and disables push. It does not require Expo Go or a running laptop. If EAS asks for an Android keystore on the first build, generate one for this project.");
  run("Build the signed Android APK on EAS", eas, ["build", "--platform", "android", "--profile", profile, "--wait"], { env });
  console.log("\nUse the real APK installation link printed by EAS above. Keep config/eas-project.json with the project for future builds.");
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
