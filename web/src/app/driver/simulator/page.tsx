import { notFound, redirect } from "next/navigation";
import { apiOrigin, backend, currentUser } from "@/lib/backend";
import type { DriverChargingState, Station } from "../../../../../contracts/account";
import { AutomaticSimulatorPanel } from "@/features/driver/automatic-simulator-panel";
import "../../simulator.css";

export default async function SimulatorPage({ searchParams }: { searchParams: Promise<{ station?: string }> }) {
  const user = await currentUser();
  if (!user) redirect("/");
  const [stateResponse, stationsResponse] = await Promise.all([backend("/driver/charging-sessions"), backend("/driver/stations")]);
  if (!stateResponse.ok || !stationsResponse.ok) throw new Error("Simulator service unavailable.");
  const state: DriverChargingState = await stateResponse.json();
  if (!state.canTestSimulator) notFound();
  const stations: Station[] = (await stationsResponse.json()).filter((station: Station) => station.isDemo && /^SIM-\d{3}$/.test(station.code));
  const requested = (await searchParams).station;
  const selected = stations.find(station => station.code === requested) ?? stations[0];
  if (!selected) notFound();
  const socket = new URL("/ws/cs/", apiOrigin());
  socket.protocol = socket.protocol === "https:" ? "wss:" : "ws:";
  return <AutomaticSimulatorPanel stations={stations} selected={selected.code} socketBase={socket.href}
    capacityKWh={user.batteryCapacityKWh} language={user.language}/>;
}
