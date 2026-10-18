import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { compile } from "./helpers/compile.mjs";

test("app config keeps independent EVCharging identifiers and native assets", () => {
  const config = compile("../../app.config.ts").default;
  assert.equal(config.ios.bundleIdentifier, "al.evcharging.mobile");
  assert.equal(config.android.package, "al.evcharging.mobile");
  assert.equal(config.scheme, "evcharging"); assert.equal(config.android.softwareKeyboardLayoutMode, "resize");
  assert.ok(!config.android.blockedPermissions.includes("android.permission.ACCESS_FINE_LOCATION"));
  assert.ok(!config.android.blockedPermissions.includes("android.permission.ACCESS_COARSE_LOCATION"));
  assert.ok(config.android.blockedPermissions.includes("android.permission.ACCESS_BACKGROUND_LOCATION"));
  const location = config.plugins.find(item => Array.isArray(item) && item[0] === "expo-location")[1];
  assert.match(location.locationWhenInUsePermission, /charging station/);
  assert.equal(location.locationAlwaysPermission, false);
  assert.equal(location.locationAlwaysAndWhenInUsePermission, false);
  assert.equal(location.isIosBackgroundLocationEnabled, false);
  assert.equal(location.isAndroidBackgroundLocationEnabled, false);
  assert.equal(location.isAndroidForegroundServiceEnabled, false);
  assert.ok(config.plugins.some(item => Array.isArray(item) && item[0] === "expo-notifications" && item[1].defaultChannel === "charging"));
  assert.ok(config.plugins.includes("@react-native-community/datetimepicker"), "the native date picker plugin must be registered explicitly in dynamic Expo config");
  const icon = readFileSync(new URL("../assets/icon.png", import.meta.url));
  assert.equal(icon.readUInt32BE(16), 1024); assert.equal(icon.readUInt32BE(20), 1024); assert.equal(icon[25], 2, "iOS icon must be RGB without an alpha channel");
  const foreground = readFileSync(new URL("../assets/adaptive-icon.png", import.meta.url)); assert.equal(foreground[25], 6);
});
test("preview builds use installable APKs; store builds use Android App Bundles", () => {
  const eas = JSON.parse(readFileSync(new URL("../eas.json", import.meta.url), "utf8"));
  assert.equal(eas.build.preview.android.buildType, "apk"); assert.equal(eas.build.production.android.buildType, "app-bundle");
  assert.equal(eas.build["preview-ui"].extends, "preview");
  assert.equal(eas.build["preview-ui"].env.EXPO_PUBLIC_API_BASE_URL, "https://vcharging-api-demo.onrender.com");
  assert.equal(eas.build.simulator.ios.simulator, true); assert.equal(eas.build.development.developmentClient, true);
  assert.equal(eas.cli.appVersionSource, "remote"); assert.equal(eas.build.production.autoIncrement, true);
});
