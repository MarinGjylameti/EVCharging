import type { DriverChargingSession } from "../../../../contracts/account";

export type ChargingEstimateReason = "notCharging" | "stale" | "missingCapacity" | "missingBattery" | "staleBattery" | "missingPower" | "partialPower";
export type ChargingEstimate = { minutes: number; reason: null } | { minutes: null; reason: ChargingEstimateReason };

function fresh(until: string | null | undefined, now: number) {
  const deadline = until ? Date.parse(until) : NaN;
  return Number.isFinite(now) && Number.isFinite(deadline) && now < deadline;
}

export function chargingEstimate(session: DriverChargingSession, now: number, stale: boolean): ChargingEstimate {
  if (session.stoppedAt || session.status !== "Charging" || session.faultCode && session.faultCode !== "NoError")
    return { minutes: null, reason: "notCharging" };
  if (stale || !Number.isFinite(now) || now <= 0) return { minutes: null, reason: "stale" };
  const capacity = session.batteryCapacityKWh;
  if (capacity == null || !Number.isFinite(capacity) || capacity <= 0 || capacity > 1000)
    return { minutes: null, reason: "missingCapacity" };
  const soc = session.stateOfCharge;
  if (soc == null || !Number.isFinite(soc) || soc < 0 || soc > 100)
    return { minutes: null, reason: "missingBattery" };
  if (!fresh(session.stateOfChargeValidUntil, now)) return { minutes: null, reason: "staleBattery" };
  if (soc === 100) return { minutes: 0, reason: null };
  const power = session.powerKW;
  if (power == null || !Number.isFinite(power) || power <= 0 || !fresh(session.powerValidUntil, now))
    return { minutes: null, reason: "missingPower" };
  if (session.powerScope !== "Total") return { minutes: null, reason: "partialPower" };
  // The last reported SoC is the baseline. Elapsed time and delivered energy do
  // not invent battery progress or account for losses and charging taper.
  return { minutes: Math.ceil(capacity * (100 - soc) / 100 / power * 60), reason: null };
}

export function formatChargingEstimate(minutes: number) {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours ? `${hours} h${rest ? ` ${rest} min` : ""}` : `${rest} min`;
}
