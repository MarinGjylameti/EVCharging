import type { ExpoConfig } from "expo/config";
import { existsSync } from "node:fs";
import { resolve } from "node:path";
import { buildEnvironment } from "./config/build-environment.cjs";

const environment = buildEnvironment(process.env);
const localHttp = !["preview", "production"].includes(environment.profile) && environment.api.startsWith("http:");
const googleServices = process.env.GOOGLE_SERVICES_JSON || "./google-services.json";
const config: ExpoConfig = {
  name: "EVCharging", slug: "evcharging", version: "0.1.0",
  platforms: ["ios", "android"], orientation: "portrait", userInterfaceStyle: "light",
  scheme: "evcharging", icon: "./assets/icon.png",
  ios: {
    supportsTablet: true, bundleIdentifier: "al.evcharging.mobile",
    infoPlist: {
      ITSAppUsesNonExemptEncryption: false,
      ...(localHttp ? {
        NSAppTransportSecurity: { NSAllowsArbitraryLoads: true, NSAllowsLocalNetworking: true },
        NSLocalNetworkUsageDescription: "Lidhja me serverin lokal për provën e EVCharging / Connect to the local EVCharging test server.",
      } : {}),
    },
  },
  android: {
    package: "al.evcharging.mobile", softwareKeyboardLayoutMode: "resize",
    ...(existsSync(resolve(__dirname, googleServices)) ? { googleServicesFile: googleServices } : {}),
    adaptiveIcon: { foregroundImage: "./assets/adaptive-icon.png", backgroundColor: "#15372e" },
    blockedPermissions: ["android.permission.RECORD_AUDIO", "android.permission.CAMERA", "android.permission.ACCESS_FINE_LOCATION", "android.permission.ACCESS_COARSE_LOCATION", "android.permission.READ_EXTERNAL_STORAGE", "android.permission.WRITE_EXTERNAL_STORAGE"],
  },
  plugins: [
    "expo-secure-store", "expo-dev-client",
    ["expo-splash-screen", { image: "./assets/splash-mark.png", imageWidth: 160, resizeMode: "contain", backgroundColor: "#15372e" }],
    ["expo-notifications", { icon: "./assets/notification-icon.png", color: "#226947", defaultChannel: "charging" }],
  ],
  extra: { eas: { ...(environment.projectId ? { projectId: environment.projectId } : {}) } },
};
export default config;
