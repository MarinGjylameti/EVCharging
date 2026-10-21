import { useEffect, useRef, useState } from "react";
import type { HistoryFilters } from "../../shared/history";
import { createHistoryQuery, historyQuery, type HistoryQueryState } from "../../shared/history-query";
import type { DriverHistoryPage } from "../../contracts/account";
import { request } from "./api";
import { useForegroundPoll } from "./use-foreground-poll";

export function useHistoryPage(token: string, filters: HistoryFilters, page: number, bucket: string) {
  const key = historyQuery(filters, page, bucket);
  const [state, setState] = useState<HistoryQueryState>({ key: "", data: null, pending: true, error: "" });
  const loader = useRef<{ token: string; query: ReturnType<typeof createHistoryQuery> } | null>(null);
  if (!loader.current || loader.current.token !== token) {
    loader.current?.query.cancel();
    loader.current = { token, query: createHistoryQuery((query, signal) => request<DriverHistoryPage>(`/driver/history/search?${query}`, token, "GET", undefined, signal), value => setState({ ...value, key: `${token}:${value.key}` })) };
  }
  useEffect(() => () => loader.current?.query.cancel(), [token]);
  useForegroundPoll(signal => loader.current!.query.load(key, signal), 15000, true, `${token}:${key}`);
  const identity = `${token}:${key}`;
  return { data: state.key === identity ? state.data : null, pending: state.key !== identity || state.pending,
    error: state.key === identity ? state.error : "", reload: () => loader.current!.query.load(key) };
}
