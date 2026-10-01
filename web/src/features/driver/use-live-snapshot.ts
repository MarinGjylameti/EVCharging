"use client";

import { useCallback, useEffect, useRef, useState } from "react";

// One stream and one in-flight snapshot per mounted view. Events are coalesced
// without postponing refresh forever when accelerated simulators send rapidly.
export function useLiveSnapshot<T>(initial: T, url: string, stationId?: string) {
  const [snapshot, setSnapshot] = useState(initial);
  const [connected, setConnected] = useState(false);
  const [unavailable, setUnavailable] = useState(false);
  const revision = useRef(0);
  const replaceSnapshot = useCallback((value: T) => {
    revision.current++;
    setSnapshot(value);
  }, []);

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
      const requestedRevision = revision.current;
      try {
        const response = await fetch(url, { cache: "no-store", signal: abort.signal });
        if (response.status === 401 || response.status === 403) {
          events.close(); window.location.assign("/"); return;
        }
        if (!response.ok) throw new Error("Snapshot unavailable");
        const data: T = await response.json();
        if (!stopped && requestedRevision === revision.current) { setSnapshot(data); setUnavailable(false); }
        else if (!stopped) pending = true;
      } catch { if (!stopped) setUnavailable(true); }
      finally {
        busy = false;
        if (pending && !stopped) { pending = false; schedule(); }
      }
    }
    function schedule() {
      if (timer || stopped) return;
      timer = setTimeout(() => { timer = undefined; void refresh(); }, 250);
    }
    events.addEventListener("ready", () => { if (!stopped) { setConnected(true); schedule(); } });
    events.addEventListener("changed", event => {
      if (stationId) {
        try { if (JSON.parse((event as MessageEvent).data).stationId !== stationId) return; }
        catch { /* A malformed hint still triggers an authorized snapshot. */ }
      }
      schedule();
    });
    events.onerror = () => { if (!stopped) setConnected(false); };
    const fallback = setInterval(() => { void refresh(); }, 30_000);
    const visible = () => { if (!document.hidden) schedule(); };
    document.addEventListener("visibilitychange", visible);
    void refresh();
    return () => {
      stopped = true; events.close(); abort.abort();
      if (timer) clearTimeout(timer);
      clearInterval(fallback); document.removeEventListener("visibilitychange", visible);
    };
  }, [url, stationId]);

  return { snapshot, setSnapshot: replaceSnapshot, connected, unavailable };
}
