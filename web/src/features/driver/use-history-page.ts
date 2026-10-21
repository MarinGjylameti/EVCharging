"use client";
import { useEffect, useRef, useState } from "react";
import type { DriverHistoryPage } from "../../../../contracts/account";
import type { HistoryFilters } from "../../../../shared/history";
import { createHistoryQuery, historyQuery, type HistoryQueryState } from "../../../../shared/history-query";

export function useHistoryPage(initial: DriverHistoryPage, filters: HistoryFilters, page: number, bucket: string) {
  const key = historyQuery(filters, page, bucket);
  const [state, setState] = useState<HistoryQueryState>({ key, data: initial, pending: false, error: "" });
  const loader = useRef<ReturnType<typeof createHistoryQuery> | null>(null);
  if (!loader.current) loader.current = createHistoryQuery(async (query, signal) => {
    const response = await fetch(`/api/driver/history/search?${query}`, { signal, cache: "no-store" });
    if (!response.ok) throw new Error(response.status === 400 ? "invalid_history_filter" : "service_unavailable");
    return await response.json() as DriverHistoryPage;
  }, setState, { key, data: initial, pending: false, error: "" });
  const mounted = useRef(false);
  useEffect(() => {
    if (mounted.current) void loader.current!.load(key);
    mounted.current = true;
    return () => loader.current!.cancel();
  }, [key]);
  return { data: state.key === key ? state.data : null, pending: state.key !== key || state.pending,
    error: state.key === key ? state.error : "", reload: () => loader.current!.load(key) };
}
