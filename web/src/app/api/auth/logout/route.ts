import { NextResponse } from "next/server";
import { backend, sameOrigin, sessionCookie } from "@/lib/backend";

export async function POST(request: Request) {
  if (!sameOrigin(request)) return NextResponse.json({ code: "invalid_origin" }, { status: 403 });
  try {
    const response = await backend("/auth/logout", { method: "POST" });
    if (!response.ok && response.status !== 401) return NextResponse.json({ code: "logout_failed" }, { status: 503 });
    const result = NextResponse.json({ ok: true });
    result.cookies.set(sessionCookie, "", { httpOnly: true, sameSite: "strict", secure: process.env.NODE_ENV === "production", path: "/", maxAge: 0 });
    return result;
  } catch {
    return NextResponse.json({ code: "service_unavailable" }, { status: 503 });
  }
}
