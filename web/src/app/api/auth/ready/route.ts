import { apiOrigin } from "@/lib/backend";

export const dynamic = "force-dynamic";

export async function GET() {
  try {
    const response = await fetch(`${apiOrigin()}/health/ready`, {
      cache: "no-store", signal: AbortSignal.timeout(8_000),
    });
    // Render can return its HTML loading page during spin-up. A successful
    // HTTP response alone does not mean the application or database is ready.
    const payload = response.ok ? await response.json().catch(() => null) : null;
    if (payload?.status === "ready") {
      return Response.json({ status: "ready" }, { headers: { "Cache-Control": "no-store" } });
    }
  } catch {
    // A bounded probe lets the browser show progress instead of holding one
    // request open for the whole cold start.
  }
  return Response.json({ status: "unavailable" }, { status: 503, headers: { "Cache-Control": "no-store", "Retry-After": "2" } });
}
