import * as SecureStore from "expo-secure-store";
import { apiBase, validSession } from "./config";

const key = "evcharging.session.v1";
const storageOptions = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };
export type StoredSession = { token: string; expiresAt: string };
let expired: ((token: string) => void | Promise<void>) | undefined;
export function subscribeSessionExpiry(handler: (token: string) => void | Promise<void>) {
  expired = handler;
  return () => { if (expired === handler) expired = undefined; };
}
export async function readSession(): Promise<StoredSession | null> {
  const raw = await SecureStore.getItemAsync(key, storageOptions);
  if (!raw) return null;
  let data: unknown;
  try { data = JSON.parse(raw); } catch { data = null; }
  if (!validSession(data)) { await clearSession(); return null; }
  return data;
}
export async function saveSession(value: StoredSession) {
  if (!validSession(value)) throw new Error("session_expired");
  await SecureStore.setItemAsync(key, JSON.stringify(value), storageOptions);
}
export const clearSession = () => SecureStore.deleteItemAsync(key, storageOptions);

export class ApiError extends Error {
  constructor(public code: string, public status: number, public traceId?: string) { super(code); this.name = "ApiError"; }
}

export async function request<T>(path: string, token: string | null, method = "GET", body?: unknown, signal?: AbortSignal): Promise<T> {
  const origin = apiBase(process.env.EXPO_PUBLIC_API_BASE_URL, typeof __DEV__ !== "undefined" && __DEV__);
  if (!/^\/(auth|me|driver)(\/|$|\?)/.test(path) || path.includes("\\") || path.includes("#") || /(?:^|\/)\.\.(?:\/|$|\?)/.test(path)) throw new Error("configuration");
  const controller = new AbortController();
  const abort = () => controller.abort();
  if (signal?.aborted) abort();
  signal?.addEventListener("abort", abort, { once: true });
  const timer = setTimeout(abort, 12000);
  try {
    const response = await fetch(`${origin}/api/v1${path}`, {
      method, headers: { Accept: "application/json", ...(body === undefined ? {} : { "Content-Type": "application/json" }), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }), signal: controller.signal,
    });
    if (!response.ok) {
      if (response.status === 401 && token) { await expired?.(token); throw new ApiError("session_expired", 401); }
      const payload = await response.json().catch(() => ({}));
      throw new ApiError(response.status === 429 ? "rateLimit" : typeof payload?.code === "string" ? payload.code : "service_unavailable", response.status,
        typeof payload?.traceId === "string" ? payload.traceId : undefined);
    }
    if (response.status === 204) return undefined as T;
    if (!response.headers.get("content-type")?.includes("application/json")) throw new Error("service_unavailable");
    return await response.json() as T;
  } catch (error) {
    if (signal?.aborted) throw Object.assign(new Error("cancelled"), { name: "AbortError" });
    if (error instanceof TypeError || error instanceof SyntaxError || (error instanceof Error && error.name === "AbortError")) throw new Error("service_unavailable");
    throw error;
  } finally { clearTimeout(timer); signal?.removeEventListener("abort", abort); }
}
