import type { Notification } from "../../contracts/account";
import { historyDay } from "../../shared/history";

const clock = new Intl.DateTimeFormat("en-GB", { timeZone: "Europe/Tirane", hour: "2-digit", minute: "2-digit", hourCycle: "h23" });
export const notificationDay = (value: string) => historyDay(value) ?? "unknown";
export function notificationTime(value: string) {
  const date = new Date(value);
  return Number.isFinite(date.getTime()) ? clock.format(date) : "—";
}
export function notificationPage(items: Notification[], day: string, page: number) {
  const sorted = [...items].sort((a, b) => (Date.parse(b.createdAt) || 0) - (Date.parse(a.createdAt) || 0) || b.id.localeCompare(a.id));
  const days = [...new Set(sorted.map(item => notificationDay(item.createdAt)))];
  const filtered = sorted.filter(item => !day || notificationDay(item.createdAt) === day);
  const pages = Math.max(1, Math.ceil(filtered.length / 8));
  const current = Math.max(1, Math.min(pages, page));
  return { days, total: filtered.length, page: current, pages, items: filtered.slice((current - 1) * 8, current * 8) };
}
