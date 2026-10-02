function pause(milliseconds: number, signal: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    if (signal.aborted) throw signal.reason;
    const aborted = () => { clearTimeout(timer); reject(signal.reason); };
    const timer = setTimeout(() => { signal.removeEventListener("abort", aborted); resolve(); }, milliseconds);
    signal.addEventListener("abort", aborted, { once: true });
  });
}

export async function waitForApi(signal: AbortSignal, onWaiting: () => void, {
  fetcher = fetch, now = Date.now, sleep = pause, budgetMs = 120_000,
}: {
  fetcher?: typeof fetch; now?: () => number;
  sleep?: (milliseconds: number, signal: AbortSignal) => Promise<void>; budgetMs?: number;
} = {}) {
  const deadline = now() + budgetMs;
  while (now() < deadline) {
    if (signal.aborted) throw signal.reason;
    const attempt = new AbortController();
    const aborted = () => attempt.abort(signal.reason);
    signal.addEventListener("abort", aborted, { once: true });
    const timer = setTimeout(() => attempt.abort(), Math.min(12_000, deadline - now()));
    try {
      const response = await fetcher("/api/auth/ready", { cache: "no-store", signal: attempt.signal });
      const payload = response.ok ? await response.json().catch(() => null) : null;
      if (payload?.status === "ready") return true;
    } catch {
      if (signal.aborted) throw signal.reason;
    } finally {
      clearTimeout(timer); signal.removeEventListener("abort", aborted);
    }
    if (now() >= deadline) break;
    onWaiting();
    await sleep(Math.min(2_000, deadline - now()), signal);
  }
  return false;
}
