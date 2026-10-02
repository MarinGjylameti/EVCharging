"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { ChargingRealtimeEvent } from "../../../../contracts/account";
import type { LivePolicy } from "./live-update-policy";

// One stream and one in-flight snapshot per view. Coalesce bursts within a task,
// then fetch immediately; changes during a request always get a trailing fetch.
export function useLiveSnapshot<T>(initial: T, url: string, stationId?: string, policy?: LivePolicy<T>) {
  const [snapshot, setSnapshot] = useState(initial);
  const [connected, setConnected] = useState(false);
  const [unavailable, setUnavailable] = useState(false);
  const revision = useRef(0);
  const current = useRef(initial);
  const replaceSnapshot = useCallback((value: T) => {
    revision.current++;
    current.current = value;
    setSnapshot(value);
  }, []);

  useEffect(() => {
    let stopped = false;
    let busy = false;
    let pending = false;
    const pendingUrls = new Set<string>();
    let streamLive = false;
    let lastEventAt = Date.now();
    let lastRefreshAt = 0;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const abort = new AbortController();
    let events: EventSource;
    setConnected(false);
    async function refresh(requestUrl = url) {
      if (stopped || document.hidden) return;
      if (busy) { pendingUrls.add(requestUrl); return; }
      busy = true;
      const requestedRevision = revision.current;
      const requestAbort = new AbortController();
      const expired = setTimeout(() => requestAbort.abort(), 10_000);
      try {
        const response = await fetch(requestUrl, {
          cache: "no-store", signal: AbortSignal.any([abort.signal, requestAbort.signal]),
        });
        if (response.status === 401 || response.status === 403) {
          stopped = true; events.close(); setConnected(false); window.location.assign("/"); return;
        }
        if (!response.ok) throw new Error("Snapshot unavailable");
        const data: T = await response.json();
        if (!stopped && (policy || requestedRevision === revision.current)) {
          const next = policy ? policy.merge(current.current, data, requestUrl) : data;
          current.current = next; setSnapshot(next); setUnavailable(false); lastRefreshAt = Date.now();
        }
        else if (!stopped) pending = true;
      } catch { if (!stopped) setUnavailable(true); }
      finally {
        clearTimeout(expired);
        busy = false;
        if (pending && !stopped) { pending = false; schedule(); }
        else if (pendingUrls.size && !stopped) schedule();
      }
    }
    function schedule(requestUrl?: string) {
      if (stopped || document.hidden) return;
      if (requestUrl) pendingUrls.add(requestUrl);
      if (!pendingUrls.size) pendingUrls.add(url);
      if (busy) return;
      if (timer !== undefined) return;
      timer = setTimeout(() => {
        timer = undefined;
        const next = pendingUrls.has(url) ? url : pendingUrls.values().next().value ?? url;
        if (next === url) pendingUrls.clear(); else pendingUrls.delete(next);
        void refresh(next);
      }, 0);
    }
    function connect() {
      events = new EventSource("/api/driver/events");
      lastEventAt = Date.now();
      const alive = () => {
        if (stopped) return;
        lastEventAt = Date.now(); streamLive = true; setConnected(true);
      };
      events.addEventListener("ready", () => { alive(); schedule(url); });
      events.addEventListener("heartbeat", alive);
      events.addEventListener("changed", event => {
        alive();
        let hint: unknown;
        try { hint = JSON.parse((event as MessageEvent).data); } catch { /* Resync below. */ }
        if (stationId) {
          try {
            // Empty/legacy hints mean "resync". Only ignore an explicit other point.
            if (hint && typeof hint === "object" && "stationId" in hint
              && typeof hint.stationId === "string" && hint.stationId.length > 0
              && hint.stationId.toLowerCase() !== stationId.toLowerCase()) return;
          } catch { /* Malformed hints still trigger an authorized snapshot. */ }
        }
        if (policy && hint && typeof hint === "object" && "eventType" in hint) {
          const update = policy.event(current.current, hint as ChargingRealtimeEvent);
          if (update && "snapshot" in update) { replaceSnapshot(update.snapshot); setUnavailable(false); }
          else if (update) schedule(update.refreshUrl);
        } else schedule(url);
      });
      events.onerror = () => {
        if (stopped) return;
        streamLive = false; setConnected(false); schedule();
      };
    }
    function recover() {
      if (stopped || document.hidden) return;
      if (Date.now() - lastEventAt > 25_000) {
        events.close(); streamLive = false; setConnected(false); connect();
      }
      if (!streamLive || lastRefreshAt === 0 || !policy && Date.now() - lastRefreshAt >= 30_000) schedule(url);
    }
    connect();
    const fallback = setInterval(recover, 5_000);
    const visible = () => { if (!document.hidden) { recover(); schedule(url); } };
    document.addEventListener("visibilitychange", visible);
    window.addEventListener("online", visible);
    void refresh();
    return () => {
      stopped = true; events.close(); abort.abort();
      if (timer !== undefined) clearTimeout(timer);
      clearInterval(fallback); document.removeEventListener("visibilitychange", visible);
      window.removeEventListener("online", visible);
    };
  }, [url, stationId, policy, replaceSnapshot]);

  return { snapshot, setSnapshot: replaceSnapshot, connected, unavailable };
}
