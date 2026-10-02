import { cookies } from "next/headers";
import { apiOrigin, sessionCookie } from "@/lib/backend";

export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const token = (await cookies()).get(sessionCookie)?.value;
  if (!token) return new Response(null, { status: 401 });
  try {
    const upstream = await fetch(`${apiOrigin()}/api/v1/driver/stations/events`, {
      headers: { Authorization: `Bearer ${token}` }, cache: "no-store", signal: request.signal,
    });
    if (!upstream.ok) return new Response(null, { status: upstream.status });
    return new Response(upstream.body, { headers: {
      "Content-Type": "text/event-stream", "Cache-Control": "no-store, no-transform",
      "X-Accel-Buffering": "no",
      "Content-Encoding": "identity",
    } });
  } catch {
    return new Response(null, { status: 503 });
  }
}
