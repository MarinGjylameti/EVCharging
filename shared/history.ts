import type { ChargingHistory, Language } from "../contracts/account";

export type HistoryPeriod = "7d" | "30d" | "6m" | "custom";
export type HistoryMetric = "energy" | "cost" | "charges";
export type HistoryFilters = {
  period: HistoryPeriod; from: string; until: string; stationId: string;
  energy: "all" | "small" | "medium" | "large"; currency: string;
};
export type HistoryRange = { from: string; until: string };
export type HistoryBucket = {
  key: string; date: string; from: string; until: string;
  charges: number; energyKWh: number; costs: Record<string, number>;
};
export type HistoryMonth = HistoryBucket;
export const historyPageSize = 8;
export const defaultHistoryFilters: HistoryFilters = {
  period: "7d", from: "", until: "", stationId: "", energy: "all", currency: "",
};
const calendar = new Intl.DateTimeFormat("en", {
  timeZone: "Europe/Tirane", year: "numeric", month: "2-digit", day: "2-digit",
});
const dayMs = 86_400_000;

export function historyDay(value: string): string | null {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return null;
  const parts = calendar.formatToParts(date);
  return ["year", "month", "day"].map(type => parts.find(part => part.type === type)!.value).join("-");
}
function validDay(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(value)
    && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value;
}
export function moveHistoryDay(value: string, days: number): string {
  return new Date(Date.parse(value) + days * dayMs).toISOString().slice(0, 10);
}
export function historyBounds(history: ChargingHistory[]): HistoryRange | null {
  const days = history.map(item => historyDay(item.endedAt)).filter((key): key is string => key !== null).sort();
  return days.length ? { from: days[0], until: days[days.length - 1] } : null;
}
// Presets end at the latest loaded completion, rather than inventing newer activity.
export function historyRange(history: ChargingHistory[], filters: HistoryFilters): HistoryRange | null {
  if (filters.period === "custom") {
    return validDay(filters.from) && validDay(filters.until) && filters.from <= filters.until
      ? { from: filters.from, until: filters.until } : null;
  }
  const bounds = historyBounds(history);
  if (!bounds) return null;
  if (filters.period !== "6m") return { from: moveHistoryDay(bounds.until, filters.period === "7d" ? -6 : -29), until: bounds.until };
  const [year, month] = bounds.until.split("-").map(Number);
  return { from: new Date(Date.UTC(year, month - 6, 1)).toISOString().slice(0, 10), until: bounds.until };
}
export function filterHistory(history: ChargingHistory[], filters: HistoryFilters, range: HistoryRange | null): ChargingHistory[] {
  if (!range) return [];
  return history.filter(item => {
    const day = historyDay(item.endedAt);
    return day !== null && day >= range.from && day <= range.until
      && (!filters.stationId || item.stationId === filters.stationId)
      && (!filters.currency || item.currency === filters.currency)
      && (filters.energy === "all" || (filters.energy === "small" && item.energyKWh <= 10)
        || (filters.energy === "medium" && item.energyKWh > 10 && item.energyKWh <= 30)
        || (filters.energy === "large" && item.energyKWh > 30));
  }).sort((a, b) => Date.parse(b.endedAt) - Date.parse(a.endedAt) || b.id.localeCompare(a.id));
}
function bucket(from: string, until: string): HistoryBucket {
  return { key: from, date: `${from}T12:00:00Z`, from, until, charges: 0, energyKWh: 0, costs: {} };
}
// Calendar-day arithmetic keeps DST days intact. Each completion appears in one bucket;
// costs in different currencies are never added together.
export function buildHistoryBuckets(history: ChargingHistory[], range: HistoryRange, monthly = false) {
  const dayCount = (Date.parse(range.until) - Date.parse(range.from)) / dayMs + 1;
  const granularity: "month" | "week" | "day" = monthly || dayCount > 93 ? "month" : dayCount > 31 ? "week" : "day";
  const buckets: HistoryBucket[] = [];
  let from = range.from;
  while (from <= range.until) {
    let next: string;
    if (granularity === "month") {
      const [year, month] = from.split("-").map(Number);
      next = new Date(Date.UTC(year, month, 1)).toISOString().slice(0, 10);
    } else next = moveHistoryDay(from, granularity === "week" ? 7 : 1);
    buckets.push(bucket(from, next > range.until ? range.until : moveHistoryDay(next, -1)));
    from = next;
  }
  for (const charge of history) {
    const day = historyDay(charge.endedAt);
    if (!day) continue;
    const item = buckets.find(candidate => day >= candidate.from && day <= candidate.until);
    if (!item) continue;
    item.charges++;
    item.energyKWh += charge.energyKWh;
    item.costs[charge.currency] = (item.costs[charge.currency] ?? 0) + charge.totalCost;
  }
  return { buckets, granularity };
}
export function buildHistoryMonths(history: ChargingHistory[]): HistoryMonth[] {
  const range = historyRange(history, { ...defaultHistoryFilters, period: "6m" });
  return range ? buildHistoryBuckets(history, range, true).buckets.map(item => ({ ...item, key: item.from.slice(0, 7) })) : [];
}
export function summarizeHistory(history: ChargingHistory[]) {
  return history.reduce((total, item) => {
    total.energyKWh += item.energyKWh;
    total.costs[item.currency] = (total.costs[item.currency] ?? 0) + item.totalCost;
    return total;
  }, { charges: history.length, energyKWh: 0, costs: {} as Record<string, number> });
}
export function historyPage(history: ChargingHistory[], requestedPage: number) {
  const pages = Math.max(1, Math.ceil(history.length / historyPageSize));
  const page = Math.max(1, Math.min(requestedPage, pages));
  const offset = (page - 1) * historyPageSize;
  return { page, pages, offset, items: history.slice(offset, offset + historyPageSize) };
}
export function historyScale(values: number[]) {
  const peak = Math.max(0, ...values.filter(Number.isFinite));
  if (!peak) return { max: 1, ticks: [0, .25, .5, .75, 1] };
  const rawStep = peak / 4;
  const magnitude = 10 ** Math.floor(Math.log10(rawStep));
  const step = ([1, 2, 2.5, 5, 10].find(value => value * magnitude >= rawStep)! * magnitude);
  const max = step * 4;
  return { max, ticks: Array.from({ length: 5 }, (_, index) => Number((step * index).toPrecision(12))) };
}
// Explicit Albanian month names also work in runtimes without complete sq-AL ICU data.
const sqMonths = ["janar", "shkurt", "mars", "prill", "maj", "qershor", "korrik", "gusht", "shtator", "tetor", "nëntor", "dhjetor"];
export function historyDateLabel(value: string, language: Language, kind: "day" | "month" = "day", short = false): string {
  const date = new Date(`${value.slice(0, 10)}T12:00:00Z`);
  if (language === "sq") {
    const month = sqMonths[date.getUTCMonth()];
    return `${kind === "day" ? `${date.getUTCDate()} ` : ""}${short ? month.slice(0, 3) : month}${short ? "" : ` ${date.getUTCFullYear()}`}`;
  }
  return new Intl.DateTimeFormat("en-GB", {
    ...(kind === "day" ? { day: "numeric" } : {}), month: short ? "short" : "long",
    ...(!short ? { year: "numeric" } : {}), timeZone: "UTC",
  }).format(date);
}
