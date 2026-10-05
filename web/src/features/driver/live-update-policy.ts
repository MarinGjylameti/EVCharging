import type { ChargingCommandResult, ChargingDiagnostics, ChargingRealtimeEvent, DriverLiveSnapshot, StationCharging } from "../../../../contracts/account";

export type LiveUpdate<T> = { snapshot: T; refreshUrl?: string } | { refreshUrl: string } | null;
export type LivePolicy<T> = {
  event: (current: T, event: ChargingRealtimeEvent) => LiveUpdate<T>;
  merge: (current: T, incoming: T, url: string) => T;
};
const sessionEvents = new Set(["SessionStarted", "SessionMeasured", "SessionStatusChanged", "SessionCompleted", "SessionVehicleChanged"]);

function newerSession(current: StationCharging["session"], incoming: StationCharging["session"]) {
  if (!current) return incoming;
  if (!incoming) return null;
  if (current.id === incoming.id) return (current.version ?? 0) > (incoming.version ?? 0) ? current
    : { ...incoming, readings: incoming.readings ?? current.readings };
  return Date.parse(current.startReceivedAt ?? current.startedAt) > Date.parse(incoming.startReceivedAt ?? incoming.startedAt)
    ? current : incoming;
}

function commandIsOlder(current: ChargingCommandResult | null | undefined, incoming: ChargingCommandResult | null | undefined) {
  if (!current) return false;
  if (!incoming) return true;
  if ((current.version ?? 0) !== (incoming.version ?? 0)) return (current.version ?? 0) > (incoming.version ?? 0);
  return Date.parse(current.receivedAt) > Date.parse(incoming.receivedAt);
}

function lastFailure(diagnostics: ChargingDiagnostics | null | undefined) {
  if (diagnostics?.lastFailure !== undefined) return diagnostics.lastFailure;
  return diagnostics?.lastCommand && !diagnostics.lastCommand.accepted ? diagnostics.lastCommand : null;
}

function updateFailure(diagnostics: ChargingDiagnostics | null | undefined, command: ChargingCommandResult) {
  if (!command.accepted) return command;
  const failure = lastFailure(diagnostics);
  return failure?.action === command.action && !commandIsOlder(failure, command) ? null : failure;
}

export const chargingLivePolicy: LivePolicy<StationCharging> = {
  event(current, event) {
    const number = current.connector?.number;
    const refreshUrl = `/api/driver/stations/${current.station.id}/charging${number ? `?connector=${number}` : ""}`;
    if (number && (event.connector?.number && event.connector.number !== number
      || event.session?.connectorId && event.session.connectorId !== number)) return null;
    // Public events carry no ownership or private measurements. Resolve those
    // through the authenticated endpoint when another vehicle starts or stops.
    if (event.eventType === "StationChanged") {
      if (!event.station) return { refreshUrl };
      if (event.station.version < current.station.version) return null;
      const changed = event.station.status !== current.station.status;
      const ownActive = current.session?.stoppedAt === null;
      return { snapshot: { ...current, station: event.station },
        ...(changed || !ownActive && !current.inUse && event.station.status === "Charging" ? { refreshUrl } : {}) };
    }
    if (event.command && event.eventType === "DeviceCommandChanged") {
      if (commandIsOlder(current.diagnostics?.lastCommand, event.command)) return null;
      const connector = event.connector && (event.connector.version ?? event.entityVersion) >= (current.connector?.version ?? -1)
        ? event.connector : current.connector;
      return { snapshot: { ...current, connector,
        ...(connector ? { deviceConnected: connector.status !== "ConnectionLost" } : {}),
        diagnostics: { externalSessionId: null, meterStartWh: null, stopReason: null,
          ...current.diagnostics, lastCommand: event.command,
          lastFailure: updateFailure(current.diagnostics, event.command),
          samples: event.command.readings ?? current.diagnostics?.samples ?? [] } } };
    }
    if (event.session && sessionEvents.has(event.eventType)) {
      if (current.session?.id === event.session.id
        && (current.session.version ?? 0) >= (event.session.version ?? 0)) return null;
      const session = newerSession(current.session, event.session);
      if (session === current.session && event.session.id !== current.session?.id) return null;
      const active = session?.stoppedAt === null;
      return { snapshot: { ...current, session,
        connectionRequest: active ? null : current.connectionRequest,
        inUse: active ? false : current.inUse,
        deviceConnected: active ? session.status !== "ConnectionLost" : current.deviceConnected,
        ...(event.eventType === "SessionMeasured" && session?.readings && current.diagnostics
          ? { diagnostics: { ...current.diagnostics, samples: session.readings } } : {}),
        ...(event.eventType === "SessionStarted" && current.session?.id !== session?.id && current.diagnostics
          ? { diagnostics: { externalSessionId: null, meterStartWh: null, stopReason: null, lastCommand: null, samples: [],
            lastFailure: lastFailure(current.diagnostics)?.action === "StartTransaction" ? null : lastFailure(current.diagnostics) } }
          : event.eventType === "SessionCompleted" && current.diagnostics && lastFailure(current.diagnostics)?.action === "StopTransaction"
            ? { diagnostics: { ...current.diagnostics, lastFailure: null } } : {}),
      },
        ...(event.eventType === "SessionStarted" ? { refreshUrl } : {}) };
    }
    if (event.connector) {
      if (current.connector && (current.connector.version ?? -1) > (event.connector.version ?? event.entityVersion)) return null;
      return { snapshot: { ...current, connector: event.connector,
        deviceConnected: event.connector.status !== "ConnectionLost" },
        ...(current.session?.stoppedAt !== null ? { refreshUrl } : {}) };
    }
    if (event.station) return { snapshot: { ...current, station: event.station.version >= current.station.version ? event.station : current.station } };
    return { refreshUrl };
  },
  merge(current, incoming) {
    let diagnostics = incoming.diagnostics;
    if (current.diagnostics && diagnostics && current.session?.id === incoming.session?.id) {
      if (commandIsOlder(current.diagnostics.lastCommand, diagnostics.lastCommand)) {
        diagnostics = { ...diagnostics, lastCommand: current.diagnostics.lastCommand,
          lastFailure: lastFailure(current.diagnostics), samples: current.diagnostics.samples };
      }
      // A recovery response captured before the physical stop cannot revive
      // its resolved rejection after the completion delta has already arrived.
      if (current.session?.stoppedAt && incoming.session?.stoppedAt === null
        && (current.session.version ?? 0) > (incoming.session.version ?? 0)
        && lastFailure(diagnostics)?.action === "StopTransaction"
        && lastFailure(current.diagnostics)?.action !== "StopTransaction") {
        diagnostics = { ...diagnostics, lastFailure: lastFailure(current.diagnostics) };
      }
    }
    return { ...incoming,
      diagnostics,
      station: current.station.version > incoming.station.version ? current.station : incoming.station,
      session: incoming.inUse ? null : newerSession(current.session, incoming.session),
      ...(current.connector && (current.connector.version ?? -1) > (incoming.connector?.version ?? -1)
        ? { connector: current.connector, deviceConnected: current.deviceConnected } : {}),
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
