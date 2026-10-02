import { backend } from "@/lib/backend";

export const dynamic = "force-dynamic";

export async function GET() {
  try {
    const responses = await Promise.all([
      backend("/driver/stations"),
      backend("/driver/history"),
    ]);
    if (responses.some(response => response.status === 401 || response.status === 403)) {
      return Response.json({ code: "session_required" }, { status: 401 });
    }
    if (responses.some(response => !response.ok)) {
      return Response.json({ code: "service_unavailable" }, { status: 503 });
    }
    const [stations, history] = await Promise.all(responses.map(response => response.json()));
    return Response.json({ stations, history }, { headers: { "Cache-Control": "no-store" } });
  } catch {
    return Response.json({ code: "service_unavailable" }, { status: 503 });
  }
}
