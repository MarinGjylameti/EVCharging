import { useEffect, useRef } from "react";
import { AppState } from "react-native";

export function useForegroundPoll(refresh: (signal: AbortSignal) => Promise<void>, interval: number, enabled = true, identity = "") {
  const latest = useRef(refresh);
  latest.current = refresh;
  useEffect(() => {
    if (!enabled) return;
    let active = AppState.currentState === "active", stopped = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let controller: AbortController | undefined;
    async function poll() {
      if (!active || stopped) return;
      const current = new AbortController(); controller = current;
      try { await latest.current(current.signal); } catch { /* The screen reports its own connection error. */ }
      finally { if (!stopped && active && controller === current) timer = setTimeout(() => void poll(), interval); }
    }
    function cancel() { if (timer) clearTimeout(timer); controller?.abort(); controller = undefined; }
    if (active) void poll();
    const listener = AppState.addEventListener("change", state => {
      const next = state === "active";
      if (next === active) return;
      active = next; cancel(); if (active) void poll();
    });
    return () => { stopped = true; cancel(); listener.remove(); };
  }, [interval, enabled, identity]);
}
