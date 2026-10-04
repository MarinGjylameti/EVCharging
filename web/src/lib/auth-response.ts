import { NextResponse } from "next/server";
import type { AuthResult } from "../../../contracts/account";
import { apiOrigin, sessionCookie } from "./backend";

export async function authResponse(response: Response, allowedRoles: readonly string[]) {
  if (!response.ok) return NextResponse.json(await response.json().catch(() => ({ code: "authentication_failed" })), { status: response.status, headers: { "Cache-Control": "no-store" } });
  const auth = await response.json() as AuthResult;
  if (!allowedRoles.includes(auth.user.role)) {
    await fetch(`${apiOrigin()}/api/v1/auth/logout`, { method: "POST", headers: { Authorization: `Bearer ${auth.accessToken}` }, signal: AbortSignal.timeout(10000) });
    return NextResponse.json({ code: "unsupported_role" }, { status: 403 });
  }
  const result = NextResponse.json({ user: auth.user }, { headers: { "Cache-Control": "no-store" } });
  result.cookies.set(sessionCookie, auth.accessToken, {
    httpOnly: true, secure: process.env.NODE_ENV === "production", sameSite: "strict",
    path: "/", expires: new Date(auth.expiresAt),
  });
  return result;
}
