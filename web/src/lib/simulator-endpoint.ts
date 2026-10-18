export function simulatorSocketBase(apiBase: string, configuredBase?: string) {
  const api = new URL(apiBase);
  const defaultSocket = new URL("/ws/cs/", api.origin);
  defaultSocket.protocol = defaultSocket.protocol === "https:" ? "wss:" : "ws:";
  const socket = new URL(configuredBase?.trim() || defaultSocket.href);

  if (!["ws:", "wss:"].includes(socket.protocol) || socket.username || socket.password || socket.search || socket.hash
    || !/^\/ws\/cs\/?$/.test(socket.pathname) || (api.protocol === "https:" && socket.protocol !== "wss:")) {
    throw new Error("Invalid simulator WebSocket base URL. Use wss://host/ws/cs/ without a charge point ID, credentials, query or fragment.");
  }

  socket.pathname = "/ws/cs/";
  return socket.href;
}
