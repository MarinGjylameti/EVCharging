"use client";

import type { ChargingHistory, DriverLiveSnapshot, Station } from "../../../../contracts/account";
import { useLiveSnapshot } from "./use-live-snapshot";
import { driverLivePolicy } from "./live-update-policy";

export function useDriverLiveData(stations: Station[], history: ChargingHistory[]) {
  const live = useLiveSnapshot<DriverLiveSnapshot>({
    stations, history,
  }, "/api/driver/live", undefined, driverLivePolicy);
  return { ...live.snapshot, connected: live.connected, unavailable: live.unavailable };
}
