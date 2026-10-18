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
  const mutationRevision = useRef(0);
  const current = useRef(initial);
  const replaceSnapshot = useCallback((value: T) => {
    revision.current++;
    mutationRevision.current++;
    const next = policy ? policy.merge(current.current, value, url) : value;
    current.current = next;
    setSnapshot(next);
  }, [policy, url]);

  useEffect(() => {
    let stopped = false;
    let busy = false;
    let pending = false;
    const pendingUrls = new Set<string>();
    const seenEvents = new Set<string>();
    const stationVersions = new Map<string, number>();
    let duringRequest: ChargingRealtimeEvent[] = [];
    let overflow = false;
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
      duringRequest = [];
      overflow = false;
      const requestedRevision = revision.current;
      const requestedMutation = mutationRevision.current;
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
        if (!stopped && !overflow && (policy ? requestedMutation === mutationRevision.current : requestedRevision === revision.current)) {
          let next = policy ? policy.merge(current.current, data, requestUrl) : data;
          // Replay deltas received during recovery so an older list cannot undo
          // a station removal/addition or a just-started physical session.
          if (policy) for (const event of duringRequest) {
            const update = policy.event(next, event);
            if (update && "snapshot" in update) next = update.snapshot;
          }
          current.current = next; setSnapshot(next); setUnavailable(false); lastRefreshAt = Date.now();
        }
        else if (!stopped) pending = true;
      } catch { if (!stopped) setUnavailable(true); }
      finally {
        clearTimeout(expired);
        busy = false;
        duringRequest = [];
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
              && hint.stationId.toLowerCase() !== stationId.toLowerCase()) {
              if (policy?.refreshForRelatedEvent?.(current.current, hint as ChargingRealtimeEvent)) schedule(url);
              return;
            }
          } catch { schedule(url); return; }
        }
        try {
          if (policy && hint && typeof hint === "object" && "eventType" in hint) {
            const change = hint as ChargingRealtimeEvent;
            if (typeof change.eventId !== "string" || typeof change.stationId !== "string"
              || typeof change.eventType !== "string") { schedule(url); return; }
            if (seenEvents.has(change.eventId)) return;
            if (change.entityType === "ChargingStation" && Number.isFinite(change.entityVersion)) {
              if ((stationVersions.get(change.entityId) ?? -1) >= change.entityVersion) return;
              stationVersions.set(change.entityId, change.entityVersion);
              if (stationVersions.size > 1024) stationVersions.delete(stationVersions.keys().next().value!);
            }
            const update = policy.event(current.current, change);
            seenEvents.add(change.eventId);
            if (seenEvents.size > 512) seenEvents.delete(seenEvents.values().next().value!);
            if (update && "snapshot" in update) {
              revision.current++;
              current.current = update.snapshot; setSnapshot(update.snapshot); setUnavailable(false);
              if (busy && !overflow) {
                duringRequest.push(change);
                if (duringRequest.length > 256) { overflow = true; duringRequest = []; }
              }
              if (update.refreshUrl) schedule(update.refreshUrl);
            }
            else if (update) schedule(update.refreshUrl);
          } else schedule(url);
        } catch { schedule(url); }
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
