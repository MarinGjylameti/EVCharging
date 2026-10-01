import { NextResponse } from "next/server";
import { apiOrigin, apiTimeoutMs, sameOrigin, sessionCookie } from "@/lib/backend";
import type { AuthResult } from "../../../../../../contracts/account";
import { boundedJson } from "@/lib/request-body";

export async function POST(request: Request) {
  if (!sameOrigin(request)) return NextResponse.json({ code: "invalid_origin" }, { status: 403 });
  let payload: Record<string, unknown>;
  try {
    payload = await boundedJson(request);
    if (typeof payload.email !== "string" || typeof payload.password !== "string" || payload.email.length > 254 || payload.password.length > 128)
      throw new Error("invalid_body");
  } catch { return NextResponse.json({code:"invalid_body"}, {status:400}); }
  try {
    const response = await fetch(`${apiOrigin()}/api/v1/auth/login`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: payload.email, password: payload.password }),
      cache: "no-store", signal: AbortSignal.timeout(apiTimeoutMs)
    });
    if (!response.ok) return NextResponse.json(await response.json().catch(() => ({ code: "login_failed" })), { status: response.status });
    const auth = await response.json() as AuthResult;
    if (!["DRIVER", "ADMIN", "SUPERADMIN"].includes(auth.user.role)) {
      await fetch(`${apiOrigin()}/api/v1/auth/logout`, { method: "POST", headers: { Authorization: `Bearer ${auth.accessToken}` }, signal: AbortSignal.timeout(10000) });
      return NextResponse.json({ code: "unsupported_role" }, { status: 403 });
    }
    const result = NextResponse.json({ user: auth.user }, { headers: { "Cache-Control": "no-store" } });
    result.cookies.set(sessionCookie, auth.accessToken, {
      httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "strict",
      path: "/", expires: new Date(auth.expiresAt)
    });
    return result;
  } catch {
    return NextResponse.json({ code: "service_unavailable" }, { status: 503 });
  }
}
