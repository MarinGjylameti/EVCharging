const uuid = /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;
exports.buildEnvironment = function buildEnvironment(env) {
  const profile = env.EAS_BUILD_PROFILE || "local";
  const release = profile === "preview" || profile === "production";
  const api = (env.EXPO_PUBLIC_API_BASE_URL || "").trim();
  const projectId = (env.EXPO_PUBLIC_EAS_PROJECT_ID || "").trim();
  if (api) {
    let url;
    try { url = new URL(api); } catch { throw new Error("EXPO_PUBLIC_API_BASE_URL must be an API origin."); }
    if ((!release && !["https:", "http:"].includes(url.protocol)) || (release && url.protocol !== "https:")
      || url.username || url.password || url.search || url.hash || url.pathname !== "/") throw new Error("Use an API origin without credentials or a path; preview/production require HTTPS.");
  }
  if (projectId && !uuid.test(projectId)) throw new Error("EXPO_PUBLIC_EAS_PROJECT_ID must be the EVCharging project's UUID.");
  if (release && (!api || !projectId)) throw new Error("Preview/production require EXPO_PUBLIC_API_BASE_URL and EXPO_PUBLIC_EAS_PROJECT_ID in the selected EAS environment.");
  return { profile, projectId, api };
};
