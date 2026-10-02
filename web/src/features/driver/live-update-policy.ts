import type { ChargingRealtimeEvent, DriverLiveSnapshot, StationCharging } from "../../../../contracts/account";

export type LiveUpdate<T> = { snapshot: T } | { refreshUrl: string } | null;
export type LivePolicy<T> = {
  event: (current: T, event: ChargingRealtimeEvent) => LiveUpdate<T>;
  merge: (current: T, incoming: T, url: string) => T;
};

function newerSession(current: StationCharging["session"], incoming: StationCharging["session"]) {
  if (!current) return incoming;
  if (!incoming) return current;
  if (current.id === incoming.id) return (current.version ?? 0) > (incoming.version ?? 0) ? current : incoming;
  return Date.parse(current.startReceivedAt ?? current.startedAt) > Date.parse(incoming.startReceivedAt ?? incoming.startedAt)
    ? current : incoming;
}

export const chargingLivePolicy: LivePolicy<StationCharging> = {
  event(current, event) {
    if (event.session) {
      const session = newerSession(current.session, event.session);
      return { snapshot: { ...current, session, connectionRequest: null,
        deviceConnected: session?.status !== "ConnectionLost" } };
    }
    if (event.station) return { snapshot: { ...current, station: event.station.version >= current.station.version ? event.station : current.station } };
    return { refreshUrl: `/api/driver/stations/${current.station.id}/charging` };
  },
  merge(current, incoming) {
    return { ...incoming,
      station: current.station.version > incoming.station.version ? current.station : incoming.station,
      session: newerSession(current.session, incoming.session),
    };
  },
};

export const driverLivePolicy: LivePolicy<DriverLiveSnapshot> = {
  event(current, event) {
    if (event.eventType === "StationRemoved") return { snapshot: { ...current, stations: current.stations.filter(x => x.id !== event.stationId) } };
    if (event.station) {
      const previous = current.stations.find(x => x.id === event.stationId);
      if (previous && previous.version >= event.station.version) return null;
      return { snapshot: { ...current, stations: previous
        ? current.stations.map(x => x.id === event.stationId ? event.station! : x)
        : [...current.stations, event.station] } };
    }
    if (event.eventType === "SessionCompleted") return { refreshUrl: "/api/driver/history" };
    if (event.eventType) return null; // Measurements/claims do not change map or history.
    return { refreshUrl: "/api/driver/live" };
  },
  merge(current, incoming, url) {
    if (url === "/api/driver/history") return { ...current, history: incoming as unknown as DriverLiveSnapshot["history"] };
    return { ...incoming, stations: incoming.stations.map(station => {
      const previous = current.stations.find(x => x.id === station.id);
      return previous && previous.version > station.version ? previous : station;
    }) };
  },
};
