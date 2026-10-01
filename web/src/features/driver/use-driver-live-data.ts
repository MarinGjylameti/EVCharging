"use client";

import { useEffect, useState } from "react";
import type { ChargingHistory, DriverLiveSnapshot, Station } from "../../../../contracts/account";

export function useDriverLiveData(stations: Station[], history: ChargingHistory[]) {
  const [snapshot, setSnapshot] = useState<DriverLiveSnapshot>({
    stations, history, chargingState: { canTestSimulator: false, sessions: [] },
  });
  const [connected, setConnected] = useState(false);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    let stopped = false;
    let busy = false;
    let pending = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const abort = new AbortController();
    const events = new EventSource("/api/driver/events");

    async function refresh() {
      if (stopped || document.hidden) return;
      if (busy) { pending = true; return; }
      busy = true;
      try {
        const response = await fetch("/api/driver/live", { cache: "no-store", signal: abort.signal });
        if (response.status === 401) {
          events.close();
          window.location.assign("/");
          return;
        }
        if (!response.ok) throw new Error("Snapshot unavailable");
        const data: DriverLiveSnapshot = await response.json();
        if (!stopped) { setSnapshot(data); setUnavailable(false); }
      } catch {
        if (!stopped) setUnavailable(true);
      } finally {
        busy = false;
        if (pending && !stopped) { pending = false; schedule(); }
      }
    }
    function schedule() {
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => { void refresh(); }, 250);
    }
    events.addEventListener("ready", () => { setConnected(true); schedule(); });
    events.addEventListener("changed", schedule);
    events.onerror = () => { if (!stopped) setConnected(false); };
    const fallback = setInterval(() => { void refresh(); }, 30_000);
    const visible = () => { if (!document.hidden) schedule(); };
    document.addEventListener("visibilitychange", visible);
    void refresh();
    return () => {
      stopped = true;
      events.close();
      abort.abort();
      if (timer) clearTimeout(timer);
      clearInterval(fallback);
      document.removeEventListener("visibilitychange", visible);
    };
  }, []);

  return { ...snapshot, connected, unavailable };
}
