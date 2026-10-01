import type { Station } from "../../../contracts/account";

type Forecast = NonNullable<Station["availabilityForecast"]>;

export function remainingChargingMinutes(forecast: Forecast | null | undefined, now: number): number | null {
  if (!forecast) return null;
  const end = Date.parse(forecast.estimatedAvailableAt);
  const observed = Date.parse(forecast.observedAt);
  const maxAge = forecast.source === "Demo" ? 60 : 15;
  if (!Number.isFinite(end) || !Number.isFinite(observed) || observed > now + 60_000 ||
    now - observed > maxAge * 60_000 || end <= now || end > observed + 24 * 60 * 60_000)
    return null;
  return Math.ceil((end - now) / 60_000);
}
