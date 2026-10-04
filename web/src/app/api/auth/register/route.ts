import { NextResponse } from "next/server";
import { apiOrigin, apiTimeoutMs, sameOrigin } from "@/lib/backend";
import { authResponse } from "@/lib/auth-response";
import { boundedJson } from "@/lib/request-body";

export async function POST(request: Request) {
  if (!sameOrigin(request)) return NextResponse.json({ code: "invalid_origin" }, { status: 403 });
  let payload: Record<string, unknown>;
  const fields = { email: 254, password: 128, firstName: 80, lastName: 80, phone: 32, language: 2 };
  try {
    payload = await boundedJson(request);
    if (Object.keys(payload).some(key => !Object.hasOwn(fields, key))) throw new Error("invalid_body");
    for (const [name, max] of Object.entries(fields)) {
      const value = payload[name];
      if (typeof value !== "string" || !value.trim() || value.length > max) throw new Error("invalid_body");
    }
  } catch { return NextResponse.json({ code: "invalid_body" }, { status: 400 }); }
  try {
    const response = await fetch(`${apiOrigin()}/api/v1/auth/register`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload),
      cache: "no-store", signal: AbortSignal.any([request.signal, AbortSignal.timeout(apiTimeoutMs)]),
    });
    return await authResponse(response, ["DRIVER"]);
  } catch { return NextResponse.json({ code: "service_unavailable" }, { status: 503 }); }
}
