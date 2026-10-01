import { NextResponse } from "next/server";
import { backend, sameOrigin, sessionCookie } from "@/lib/backend";
import { boundedJson } from "@/lib/request-body";

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function POST(request: Request) {
  if (!sameOrigin(request)) return NextResponse.json({code:"invalid_origin"}, {status:403});
  let body: Record<string, unknown>;
  try { body = await boundedJson(request); }
  catch { return NextResponse.json({code:"invalid_body"}, {status:400}); }
  let path = "/admin/stations";
  let method = "POST";
  let payload: unknown;
  if (body.operation === "create") payload = body.station;
  else if ((body.operation === "update" || body.operation === "archive") &&
    typeof body.id === "string" && guid.test(body.id)) {
    path += `/${body.id}`;
    if (body.operation === "archive") {
      path += "/archive";
      payload = {version:body.version};
    } else {
      method = "PATCH";
      payload = body.station;
    }
  } else return NextResponse.json({code:"invalid_operation"}, {status:400});
  if (!payload || typeof payload !== "object" || Array.isArray(payload))
    return NextResponse.json({code:"invalid_body"}, {status:400});
  try {
    const response = await backend(path, {method, body:JSON.stringify(payload)});
    const result = NextResponse.json(await response.json().catch(() => ({code:"request_failed"})), {status:response.status});
    result.headers.set("Cache-Control", "no-store");
    if (response.status === 401) result.cookies.delete(sessionCookie);
    return result;
  } catch { return NextResponse.json({code:"service_unavailable"}, {status:503}); }
}
