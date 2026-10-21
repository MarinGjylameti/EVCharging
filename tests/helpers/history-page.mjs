// UI fixtures simulate paged server responses. HTTP/SQL correctness is covered
// separately by DriverHistorySearchTests, not by this in-memory fixture.
export function historyPageFixture(history, data, filters = data.defaultHistoryFilters, pageNumber = 1, selectedKey = "") {
  const range = data.historyRange(history, filters);
  const filtered = data.filterHistory(history, filters, range);
  const chart = range ? data.buildHistoryBuckets(filtered, range, filters.period === "6m") : { buckets: [], granularity: "day" };
  const selected = chart.buckets.find(item => item.key === selectedKey);
  const matching = selected ? filtered.filter(item => { const day = data.historyDay(item.endedAt); return day >= selected.from && day <= selected.until; }) : filtered;
  return { ...data.historyPage(matching, pageNumber), pageSize: 8, total: matching.length, summary: data.summarizeHistory(matching),
    range, bounds: data.historyBounds(history), chart, stations: [...new Map(history.map(item => [item.stationId, item])).values()],
    currencies: [...new Set(history.map(item => item.currency))].sort(), hasDemo: filtered.some(item => item.isDemo) };
}
