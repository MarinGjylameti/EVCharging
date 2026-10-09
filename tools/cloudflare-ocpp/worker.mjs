const API_ORIGIN = new URL("https://vcharging-api-demo.onrender.com");
const CHARGER_PATH = /^\/ws\/cs\/[A-Za-z0-9_-]{1,40}\/?$/;

export default {
  async fetch(request) {
    const incoming = new URL(request.url);
    const healthCheck = incoming.pathname === "/health/ready";

    if (!healthCheck && !CHARGER_PATH.test(incoming.pathname)) {
      return new Response("Not found", { status: 404 });
    }
    if (request.method !== "GET") {
      return new Response("Method not allowed", {
        status: 405,
        headers: { Allow: "GET" },
      });
    }
    if (
      !healthCheck &&
      request.headers.get("Upgrade")?.toLowerCase() !== "websocket"
    ) {
      return new Response("WebSocket upgrade required", { status: 426 });
    }

    const upstream = new URL(incoming.pathname + incoming.search, API_ORIGIN);
    const headers = new Headers(request.headers);
    headers.set("Host", API_ORIGIN.host);
    const forwarded = new Request(upstream, {
      method: "GET",
      headers,
      redirect: "manual",
    });

    try {
      // Return the original 101 response so Workers proxies the socket directly,
      // preserving its negotiated OCPP subprotocol and handling frames itself.
      return await fetch(forwarded);
    } catch {
      return new Response("Backend unavailable", {
        status: 502,
        headers: { "Cache-Control": "no-store" },
      });
    }
  },
};
