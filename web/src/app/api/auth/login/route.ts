import { NextResponse } from "next/server";
import { apiOrigin, apiTimeoutMs, sameOrigin } from "@/lib/backend";
import { authResponse } from "@/lib/auth-response";
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
      cache: "no-store", signal: AbortSignal.any([request.signal, AbortSignal.timeout(apiTimeoutMs)])
    });
    return await authResponse(response, ["DRIVER", "ADMIN", "SUPERADMIN"]);
  } catch {
    return NextResponse.json({ code: "service_unavailable" }, { status: 503 });
  }
}
