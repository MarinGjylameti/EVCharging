"use client";

import type { ChargingHistory, DriverLiveSnapshot, Station } from "../../../../contracts/account";
import { useLiveSnapshot } from "./use-live-snapshot";

export function useDriverLiveData(stations: Station[], history: ChargingHistory[]) {
  const live = useLiveSnapshot<DriverLiveSnapshot>({
    stations, history, chargingState: { canTestSimulator: false, sessions: [] },
  }, "/api/driver/live");
  return { ...live.snapshot, connected: live.connected, unavailable: live.unavailable };
}
