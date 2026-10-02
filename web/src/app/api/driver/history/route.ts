import { backend } from "@/lib/backend";
export const dynamic = "force-dynamic";
export async function GET() {
  try {
    const response = await backend("/driver/history");
    return Response.json(await response.json(), { status: response.status, headers: { "Cache-Control": "no-store" } });
  } catch { return Response.json({ code: "service_unavailable" }, { status: 503 }); }
}
