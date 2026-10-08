const { buildEnvironment } = require("../config/build-environment.cjs");
const { existsSync, readFileSync } = require("node:fs");
const { resolve } = require("node:path");
try {
  const envFile = resolve(__dirname, "..", ".env");
  if (existsSync(envFile)) process.loadEnvFile(envFile);
  buildEnvironment({ ...process.env, EAS_BUILD_PROFILE: process.env.EAS_BUILD_PROFILE || "preview" });
  const platform = process.argv[2];
  if (!["ios", "android"].includes(platform)) throw new Error("Specify ios or android.");
  if (platform === "android") {
    const firebase = resolve(__dirname, "..", process.env.GOOGLE_SERVICES_JSON || "google-services.json");
    if (!existsSync(firebase)) throw new Error("Android push requires the EVCharging Firebase google-services.json (or GOOGLE_SERVICES_JSON file variable).");
    const settings = JSON.parse(readFileSync(firebase, "utf8"));
    if (!settings.client?.some(client => client.client_info?.android_client_info?.package_name === "al.evcharging.mobile")) throw new Error("Firebase configuration must belong to al.evcharging.mobile.");
  }
  console.log(`${platform}: environment checks passed. Signing credentials and push delivery still require a device build.`);
} catch (error) { console.error(error.message); process.exitCode = 1; }
