export function apiBase(value: string | undefined, development: boolean): string {
  try {
    const url = new URL(value?.trim() || "");
    if (!["https:", ...(development ? ["http:"] : [])].includes(url.protocol)
      || url.username || url.password || url.search || url.hash || !["", "/"].includes(url.pathname)) throw new Error();
    return url.origin;
  } catch { throw new Error("configuration"); }
}

export function validSession(value: unknown, now = Date.now()): value is { token: string; expiresAt: string } {
  if (!value || typeof value !== "object") return false;
  const data = value as Record<string, unknown>;
  return typeof data.token === "string" && /^[A-Fa-f0-9]{64}$/.test(data.token)
    && typeof data.expiresAt === "string" && Number.isFinite(Date.parse(data.expiresAt)) && Date.parse(data.expiresAt) > now;
}

export const isStationId = (value: unknown): value is string => typeof value === "string"
  && /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value);
