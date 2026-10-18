const fields = {
  privacy: "EXPO_PUBLIC_PRIVACY_URL",
  deletion: "EXPO_PUBLIC_ACCOUNT_DELETION_URL",
};

// Public pages only. Credentials, user IDs and session tokens never belong here.
exports.storeLinks = function storeLinks(env, required = false) {
  const links = {};
  for (const [key, field] of Object.entries(fields)) {
    const value = (env[field] || "").trim();
    if (!value) {
      if (required) throw new Error(`${field} is required before store submission. Configure the client's published page.`);
      links[key] = "";
      continue;
    }
    let url;
    try { url = new URL(value); } catch { throw new Error(`${field} must be a public HTTPS page.`); }
    if (url.protocol !== "https:" || url.username || url.password || url.search || url.hash
      || !url.hostname.includes(".") || url.hostname.endsWith(".localhost") || url.hostname.endsWith(".local")
      || /^(127\.|10\.|192\.168\.|169\.254\.|172\.(1[6-9]|2\d|3[01])\.)/.test(url.hostname)
      || /(^|\.)(example\.(com|org|net)|invalid|test)$/.test(url.hostname))
      throw new Error(`${field} must be a public HTTPS page without credentials, query parameters or placeholders.`);
    links[key] = url.href;
  }
  return links;
};
