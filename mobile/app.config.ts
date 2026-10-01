export default {
  expo: {
    name: "EVCharging",
    slug: "evcharging",
    version: "0.1.0",
    orientation: "portrait",
    userInterfaceStyle: "light",
    scheme: "evcharging",
    ios: { supportsTablet: true, bundleIdentifier: "al.evcharging.mobile" },
    android: { package: "al.evcharging.mobile" },
    plugins: ["expo-secure-store", ["expo-notifications", { defaultChannel: "charging" }]],
    extra: { eas: { projectId: process.env.EXPO_PUBLIC_EAS_PROJECT_ID || "" } }
  }
};
