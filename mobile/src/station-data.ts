import type { DriverChargingSession, Language, Station, StationCharging } from "../../contracts/account";
import { directionsUrl, type MapProvider } from "../../shared/map-directions";

export const stationStates: Record<Station["status"], [string, string, string]> = {
  Available: ["E lirë", "Available", "#008443"], Occupied: ["E zënë", "Occupied", "#b85c00"],
  Charging: ["Në karikim", "Charging", "#1769e0"], OutOfService: ["Jashtë funksionit", "Out of service", "#d92d35"],
  Maintenance: ["Në mirëmbajtje", "Maintenance", "#8644c5"], Unknown: ["Në pritje të statusit", "Awaiting status", "#68756e"],
};
export function stationState(status: string, language: Language) {
  const item = stationStates[status as Station["status"]] ?? stationStates.Unknown;
  return { text: item[language === "sq" ? 0 : 1], color: item[2] };
}
export function selectStations(stations: Station[], city: string | null, availableOnly: boolean, power: "all" | "ac" | "fast") {
  return stations.filter(item => !item.isArchived && (!city || item.city === city)
    && (!availableOnly || item.status === "Available")
    && (power === "all" || (power === "ac" ? item.powerKW <= 22 : item.powerKW > 22)));
}
export function navigationUrls(station: Station, provider: MapProvider, ios: boolean) {
  if (!Number.isFinite(station.latitude) || !Number.isFinite(station.longitude) || Math.abs(station.latitude) > 90 || Math.abs(station.longitude) > 180) throw new Error("mapUnavailable");
  const position = `${station.latitude.toFixed(6)},${station.longitude.toFixed(6)}`;
  const fallback = directionsUrl(provider, station);
  return { primary: provider === "apple" ? fallback : ios ? `comgooglemaps://?daddr=${position}&directionsmode=driving` : `google.navigation:q=${position}&mode=d`, fallback };
}
export function ownChargingSession(snapshot: StationCharging): DriverChargingSession | null {
  return snapshot.inUse || (snapshot.connectionRequest && snapshot.session?.stoppedAt) ? null : snapshot.session;
}
export function chargingValues(session: DriverChargingSession, now: number, stale: boolean) {
  const ended = session.stoppedAt !== null;
  const powerFresh = !stale && !ended && session.status === "Charging"
    && (!session.powerValidUntil || now < Date.parse(session.powerValidUntil));
  const batteryFresh = !stale && (!session.stateOfChargeValidUntil || now < Date.parse(session.stateOfChargeValidUntil));
  return {
    energy: session.energyKWh,
    power: ended ? 0 : powerFresh ? session.powerKW : null,
    battery: batteryFresh && session.stateOfCharge !== null && session.stateOfCharge >= 0 && session.stateOfCharge <= 100 ? session.stateOfCharge : null,
    cost: session.cost,
  };
}
export function chargingDuration(session: DriverChargingSession, now: number) {
  const start = session.isDemo ? session.startReceivedAt ?? session.startedAt : session.startedAt;
  const end = session.isDemo ? session.stopReceivedAt ?? session.stoppedAt : session.stoppedAt;
  const seconds = Math.floor(((end ? Date.parse(end) : now) - Date.parse(start)) / 1000);
  if (!Number.isFinite(seconds) || seconds < 0) return "—";
  const hours = Math.floor(seconds / 3600), minutes = Math.floor(seconds % 3600 / 60), rest = seconds % 60;
  return `${hours ? `${hours}:` : ""}${String(minutes).padStart(2, "0")}:${String(rest).padStart(2, "0")}`;
}
