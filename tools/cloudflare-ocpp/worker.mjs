const API_ORIGIN = new URL("https://vcharging-api-demo.onrender.com");
const CHARGER_PATH = /^\/ws\/cs\/[A-Za-z0-9_-]{1,40}\/?$/;
const PROXY_VERSION = "evcharging-ocpp/diag-1";

function rayId(value) {
  return /^[a-f0-9]{16,32}(?:-[A-Za-z]{3})?$/.test(value ?? "") ? value : null;
}

function trace(context, stage, details = {}) {
  // Deliberately exclude query strings, credentials, cookies and OCPP payloads.
  console.log(JSON.stringify({ event: "ocpp_proxy", version: PROXY_VERSION, ...context, stage, ...details }));
}

function completed(response, context, stage) {
  trace(context, stage, {
    status: response.status,
    upstreamRay: stage === "upstream_response" ? rayId(response.headers.get("CF-Ray")) : null,
    ocpp16Selected: response.headers.get("Sec-WebSocket-Protocol") === "ocpp1.6",
  });
  // Never reconstruct a successful upgrade: keep its socket and subprotocol intact.
  if (response.status === 101) return response;
  const marked = new Response(response.body, response);
  marked.headers.set("X-OCPP-Proxy", PROXY_VERSION);
  marked.headers.set("X-OCPP-Proxy-Stage", stage);
  return marked;
}

export default {
  async fetch(request) {
    const incoming = new URL(request.url);
    const healthCheck = incoming.pathname === "/health/ready";
    const chargerPath = CHARGER_PATH.test(incoming.pathname);
    const context = {
      ray: rayId(request.headers.get("CF-Ray")),
      host: incoming.host,
      route: healthCheck ? "health" : chargerPath ? "charger" : "other",
      identity: chargerPath ? incoming.pathname.split("/")[3] : null,
      ocpp16Offered: (request.headers.get("Sec-WebSocket-Protocol") ?? "").split(",").some(value => value.trim() === "ocpp1.6"),
    };
    trace(context, "received");

    if (!healthCheck && !chargerPath) {
      return completed(new Response("Not found", { status: 404 }), context, "worker_rejected");
    }
    if (request.method !== "GET") {
      return completed(new Response("Method not allowed", {
        status: 405,
        headers: { Allow: "GET" },
      }), context, "worker_rejected");
    }
    if (
      !healthCheck &&
      request.headers.get("Upgrade")?.toLowerCase() !== "websocket"
    ) {
      return completed(new Response("WebSocket upgrade required", { status: 426 }), context, "worker_rejected");
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
      return completed(await fetch(forwarded), context, "upstream_response");
    } catch {
      return completed(new Response("Backend unavailable", {
        status: 502,
        headers: { "Cache-Control": "no-store" },
      }), context, "upstream_error");
    }
  },
};
