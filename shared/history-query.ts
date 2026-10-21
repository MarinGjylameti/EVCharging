import type { DriverHistoryPage } from "../contracts/account";
import type { HistoryFilters } from "./history";

export function historyQuery(filters: HistoryFilters, page: number, bucket: string): string {
  const pairs: [string, string][] = [["period", filters.period], ["page", String(page)], ["pageSize", "8"], ["energy", filters.energy]];
  if (filters.period === "custom") pairs.push(["from", filters.from], ["until", filters.until]);
  if (filters.stationId) pairs.push(["stationId", filters.stationId]);
  if (filters.currency) pairs.push(["currency", filters.currency]);
  if (bucket) pairs.push(["bucket", bucket]);
  return pairs.map(([key, value]) => `${key}=${encodeURIComponent(value)}`).join("&");
}
export type HistoryQueryState = { key: string; data: DriverHistoryPage | null; pending: boolean; error: string };
export function emptyHistoryPage(): DriverHistoryPage {
  return { items: [], total: 0, page: 1, pageSize: 8, pages: 1, offset: 0, bounds: null, range: null,
    summary: { charges: 0, energyKWh: 0, costs: {} }, chart: { granularity: "day", buckets: [] }, stations: [], currencies: [], hasDemo: false };
}
export function createHistoryQuery(read: (query: string, signal: AbortSignal) => Promise<DriverHistoryPage>, deliver: (value: HistoryQueryState) => void, initial?: HistoryQueryState) {
  let revision = 0, controller: AbortController | null = null;
  let state: HistoryQueryState = initial ?? { key: "", data: null, pending: false, error: "" };
  return {
    cancel() { revision++; controller?.abort(); controller = null; },
    async load(key: string, signal?: AbortSignal) {
      controller?.abort(); const current = ++revision;
      const attempt = new AbortController(); controller = attempt;
      const abort = () => attempt.abort();
      signal?.addEventListener("abort", abort, { once: true });
      if (signal?.aborted) abort();
      const active = () => revision === current && !attempt.signal.aborted;
      state = { key, data: state.key === key ? state.data : null, pending: true, error: "" };
      if (active()) deliver(state);
      try {
        const data = await read(key, attempt.signal);
        if (active()) { state = { key, data, pending: false, error: "" }; deliver(state); }
      } catch (failure) {
        if (active()) { state = { ...state, pending: false, error: failure instanceof Error ? failure.message : "genericError" }; deliver(state); }
      } finally { signal?.removeEventListener("abort", abort); }
    },
  };
}
