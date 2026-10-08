// Picker dates are calendar dates. Do not convert midnight through UTC/Tirana:
// that would move the chosen day on phones in a different time zone.
export function calendarDay(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
export function calendarDate(day: string, fallback = new Date()) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return fallback;
  const date = new Date(`${day}T12:00:00`);
  return Number.isFinite(date.getTime()) && calendarDay(date) === day ? date : fallback;
}
