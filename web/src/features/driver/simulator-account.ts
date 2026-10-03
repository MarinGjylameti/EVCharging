import type { StationCharging } from "../../../../contracts/account";

export class SimulatorAccountError extends Error {
  constructor(readonly status: number, readonly code: string) { super(code); }
}

// An unchecked account option explicitly cancels this caller's waiting request.
// It never changes the owner of an existing charge or another driver's waiting request.
export async function prepareSimulatorAccount(stationId: string, associate: boolean, request: typeof fetch = fetch) {
  const response = await request(`/api/driver/stations/${encodeURIComponent(stationId)}/charging`, {
    method: associate ? "POST" : "DELETE",
  });
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new SimulatorAccountError(response.status, data?.code ?? "association_failed");
  if (!data || data.station?.id !== stationId) throw new Error("Invalid charging association response.");
  return data as StationCharging;
}
