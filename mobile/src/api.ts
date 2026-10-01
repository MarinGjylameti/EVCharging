import * as SecureStore from "expo-secure-store";
const key = "evcharging.session.v1";
export type StoredSession = { token: string; expiresAt: string };

export async function readSession(): Promise<StoredSession | null> {
  const raw = await SecureStore.getItemAsync(key);
  if (!raw) return null;
  try {
    const data = JSON.parse(raw) as StoredSession;
    if (!/^[A-Fa-f0-9]{64}$/.test(data.token) || !Number.isFinite(Date.parse(data.expiresAt)) || Date.parse(data.expiresAt) <= Date.now()) {
      await clearSession(); return null;
    }
    return data;
  } catch { await clearSession(); return null; }
}
export const saveSession = (value: StoredSession) => SecureStore.setItemAsync(key, JSON.stringify(value));
export const clearSession = () => SecureStore.deleteItemAsync(key);

export async function request<T>(path: string, token: string | null, method = "GET", body?: unknown): Promise<T> {
  const origin = process.env.EXPO_PUBLIC_API_BASE_URL?.replace(/\/$/, "");
  if (!origin || (!__DEV__ && !origin.startsWith("https://"))) throw new Error("configuration");
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 12000);
  try {
    const response = await fetch(`${origin}/api/v1${path}`, {
      method, headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }), signal: controller.signal
    });
    if (!response.ok) {
      if (response.status === 429) throw new Error("rateLimit");
      if (response.status === 401 && token) throw new Error("session_expired");
      const payload = await response.json().catch(() => ({ code: "service_unavailable" }));
      throw new Error(payload.code || "genericError");
    }
    return response.status === 204 ? undefined as T : await response.json() as T;
  } catch (error) {
    if (error instanceof TypeError || (error instanceof Error && error.name === "AbortError")) throw new Error("service_unavailable");
    throw error;
  } finally { clearTimeout(timer); }
}
