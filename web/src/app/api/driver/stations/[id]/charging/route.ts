import { backend, sameOrigin } from "@/lib/backend";

export const dynamic = "force-dynamic";

type Context = { params: Promise<{ id: string }> };
const validId = (id: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id);

async function forward(id: string, action: "charging" | "connect" | "cancel", method: string, signal: AbortSignal) {
  if (!validId(id)) return Response.json({ code: "station_not_found" }, { status: 404 });
  try {
    const response = await backend(`/driver/stations/${id}/${action}`, { method, signal });
    return Response.json(await response.json().catch(() => ({ code: "service_unavailable" })), {
      status: response.status, headers: { "Cache-Control": "no-store" },
    });
  } catch { return Response.json({ code: "service_unavailable" }, { status: 503 }); }
}

export async function GET(request: Request, context: Context) {
  return forward((await context.params).id, "charging", "GET", request.signal);
}

export async function POST(request: Request, context: Context) {
  if (!sameOrigin(request)) return Response.json({ code: "invalid_origin" }, { status: 403 });
  return forward((await context.params).id, "connect", "POST", request.signal);
}

export async function DELETE(request: Request, context: Context) {
  if (!sameOrigin(request)) return Response.json({ code: "invalid_origin" }, { status: 403 });
  return forward((await context.params).id, "cancel", "POST", request.signal);
}
