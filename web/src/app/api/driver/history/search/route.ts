import { backend } from "@/lib/backend";
export const dynamic = "force-dynamic";
const allowed = new Set(["period", "from", "until", "stationId", "energy", "currency", "page", "pageSize", "bucket"]);
export async function GET(request: Request) {
  const search = new URL(request.url).searchParams;
  if ([...search.keys()].some(key => !allowed.has(key) || search.getAll(key).length !== 1))
    return Response.json({ code: "invalid_history_filter" }, { status: 400 });
  try {
    const response = await backend(`/driver/history/search?${search}`, { signal: request.signal });
    return Response.json(await response.json(), { status: response.status, headers: { "Cache-Control": "no-store" } });
  } catch { return Response.json({ code: "service_unavailable" }, { status: 503 }); }
}
