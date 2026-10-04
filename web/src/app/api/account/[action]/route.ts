import { NextResponse } from "next/server";
import { backend, sameOrigin, sessionCookie } from "@/lib/backend";
import { boundedJson } from "@/lib/request-body";

const actions: Record<string, { path: string; method: string }> = {
  profile: { path: "/me/profile", method: "PATCH" },
  settings: { path: "/me/settings", method: "PATCH" },
  password: { path: "/auth/password", method: "POST" }
};
export async function POST(request: Request, context: { params: Promise<{ action: string }> }) {
  if (!sameOrigin(request)) return NextResponse.json({ code:"invalid_origin" }, {status:403});
  const { action } = await context.params;
  const target = Object.hasOwn(actions, action) ? actions[action] : undefined;
  if (!target) return NextResponse.json({code:"not_found"}, {status:404});
  let body: Record<string, unknown>;
  try { body = await boundedJson(request); }
  catch { return NextResponse.json({code:"invalid_body"}, {status:400}); }
  try {
    const response = await backend(target.path, {method:target.method, body:JSON.stringify(body), signal: request.signal});
    const result = response.status === 204 ? new NextResponse(null, {status:204}) :
      NextResponse.json(await response.json().catch(() => ({code:"request_failed"})), {status:response.status});
    result.headers.set("Cache-Control", "no-store");
    if (response.status === 401 || (action === "password" && response.ok)) result.cookies.delete(sessionCookie);
    return result;
  } catch { return NextResponse.json({code:"service_unavailable"}, {status:503}); }
}
