import { apiBase } from "./config";

const pause = (milliseconds: number, signal: AbortSignal) => new Promise<void>((resolve, reject) => {
  if (signal.aborted) { reject(new Error("cancelled")); return; }
  const abort = () => { clearTimeout(timer); reject(new Error("cancelled")); };
  const timer = setTimeout(() => { signal.removeEventListener("abort", abort); resolve(); }, milliseconds);
  signal.addEventListener("abort", abort, { once: true });
});
// Retry only the idempotent health probe, never a login or registration POST.
export async function waitForMobileApi(signal: AbortSignal, onWaiting: () => void, {
  fetcher = fetch, now = Date.now, sleep = pause, budgetMs = 120000,
}: { fetcher?: typeof fetch; now?: () => number; sleep?: typeof pause; budgetMs?: number } = {}) {
  const origin = apiBase(process.env.EXPO_PUBLIC_API_BASE_URL, typeof __DEV__ !== "undefined" && __DEV__);
  const deadline = now() + budgetMs;
  while (now() < deadline) {
    if (signal.aborted) throw new Error("cancelled");
    const attempt = new AbortController();
    const abort = () => attempt.abort();
    signal.addEventListener("abort", abort, { once: true });
    const timer = setTimeout(abort, Math.min(12000, deadline - now()));
    try {
      const response = await fetcher(`${origin}/health/ready`, { headers: { Accept: "application/json" }, signal: attempt.signal });
      const payload = response.ok ? await response.json().catch(() => null) : null;
      if (signal.aborted) throw new Error("cancelled");
      if (payload?.status === "ready") return true;
    } catch { if (signal.aborted) throw new Error("cancelled"); }
    finally { clearTimeout(timer); signal.removeEventListener("abort", abort); }
    if (signal.aborted) throw new Error("cancelled");
    if (now() >= deadline) break;
    onWaiting(); await sleep(Math.min(2000, deadline - now()), signal);
  }
  return false;
}
