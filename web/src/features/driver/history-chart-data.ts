import type { ChargingHistory } from "../../../../contracts/account";

export type HistoryMonth = {
  key: string;
  date: string;
  charges: number;
  energyKWh: number;
  costs: Record<string, number>;
};

const calendar = new Intl.DateTimeFormat("en", {
  timeZone: "Europe/Tirane", year: "numeric", month: "2-digit",
});

function monthKey(value: string): string | null {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return null;
  const parts = calendar.formatToParts(date);
  return `${parts.find(part => part.type === "year")!.value}-${parts.find(part => part.type === "month")!.value}`;
}

// Bucket completed sessions in the same calendar as the history list. A charge
// belongs to its completion month; currencies stay separate when calculating cost.
export function buildHistoryMonths(history: ChargingHistory[]): HistoryMonth[] {
  const entries = history.map(charge => ({ charge, key: monthKey(charge.endedAt) }))
    .filter((entry): entry is { charge: ChargingHistory; key: string } => entry.key !== null);
  const latest = entries.map(entry => entry.key).sort().at(-1);
  if (!latest) return [];
  const [year, month] = latest.split("-").map(Number);
  const months = Array.from({ length: 6 }, (_, index) => {
    const date = new Date(Date.UTC(year, month - 6 + index, 15));
    return {
      key: date.toISOString().slice(0, 7), date: date.toISOString(),
      charges: 0, energyKWh: 0, costs: {} as Record<string, number>,
    };
  });
  const byMonth = new Map(months.map(item => [item.key, item]));
  for (const { charge, key } of entries) {
    const item = byMonth.get(key);
    if (!item) continue;
    item.charges += 1;
    item.energyKWh += charge.energyKWh;
    item.costs[charge.currency] = (item.costs[charge.currency] ?? 0) + charge.totalCost;
  }
  return months;
}
