const uuid = /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;
const { storeLinks } = require("./store-links.cjs");
exports.buildEnvironment = function buildEnvironment(env, linkedProjectId = "") {
  const profile = env.EAS_BUILD_PROFILE || "local";
  const release = ["preview", "preview-ui", "production"].includes(profile);
  const api = (env.EXPO_PUBLIC_API_BASE_URL || "").trim();
  const projectId = (env.EXPO_PUBLIC_EAS_PROJECT_ID || linkedProjectId).trim();
  if (linkedProjectId && projectId !== linkedProjectId) throw new Error("Expo project ID differs from the linked EVCharging project.");
  const pushEnabled = profile !== "preview-ui";
  if (api) {
    let url;
    try { url = new URL(api); } catch { throw new Error("EXPO_PUBLIC_API_BASE_URL must be an API origin."); }
    if ((!release && !["https:", "http:"].includes(url.protocol)) || (release && url.protocol !== "https:")
      || url.username || url.password || url.search || url.hash || url.pathname !== "/") throw new Error("Use an API origin without credentials or a path; preview/production require HTTPS.");
  }
  if (projectId && !uuid.test(projectId)) throw new Error("EXPO_PUBLIC_EAS_PROJECT_ID must be the EVCharging project's UUID.");
  if (release && (!api || !projectId)) throw new Error("Preview/production require EXPO_PUBLIC_API_BASE_URL and a linked Expo project ID (or EXPO_PUBLIC_EAS_PROJECT_ID).");
  storeLinks(env, profile === "production");
  return { profile, projectId, api, pushEnabled };
};
