import { cookies } from "next/headers";
import { cache } from "react";
import type { User } from "../../../contracts/account";

export const sessionCookie = "evcharging_session";
export const apiTimeoutMs = 90_000;

export function apiOrigin() {
  const configured = process.env.EVCHARGING_API_BASE_URL;
  if (!configured && process.env.NODE_ENV === "production") throw new Error("EVCHARGING_API_BASE_URL is required.");
  const url = new URL(configured || "http://127.0.0.1:5201");
  if (process.env.NODE_ENV === "production" && url.protocol !== "https:") throw new Error("Production API must use HTTPS.");
  return url.origin;
}

export async function backend(path: string, init: RequestInit = {}) {
  const token = (await cookies()).get(sessionCookie)?.value;
  const headers = new Headers(init.headers);
  if (token) headers.set("Authorization", `Bearer ${token}`);
  if (init.body) headers.set("Content-Type", "application/json");
  return fetch(`${apiOrigin()}/api/v1${path}`, { ...init, headers, cache: "no-store", signal: AbortSignal.timeout(apiTimeoutMs) });
}

export const currentUser = cache(async (): Promise<User | null> => {
  if (!(await cookies()).has(sessionCookie)) return null;
  const response = await backend("/me");
  if (response.status === 401) return null;
  if (!response.ok) throw new Error("Account service unavailable.");
  return response.json();
});

export function sameOrigin(request: Request) {
  const expected = process.env.EVCHARGING_WEB_ORIGIN ||
    (process.env.NODE_ENV === "production" ? "" : "http://127.0.0.1:3100");
  return Boolean(expected) && request.headers.get("origin") === new URL(expected).origin;
}
