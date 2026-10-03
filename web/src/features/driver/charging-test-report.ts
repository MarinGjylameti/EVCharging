import type { Station, StationCharging } from "../../../../contracts/account";
import type { SimulatorConfig, SimulatorLog, SimulatorState } from "./automatic-simulator";

const secretKeys = /^(password|authorization|authorizationKey|accessToken|refreshToken|token|cookie|set-cookie)$/i;

function redact(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(redact);
  if (value && typeof value === "object") {
    const fields = value as Record<string, unknown>;
    return Object.fromEntries(Object.entries(fields).map(([key, item]) => [key,
      secretKeys.test(key) || key === "value" && typeof fields.key === "string" && secretKeys.test(fields.key)
        ? "[redacted]" : redact(item)]));
  }
  if (typeof value === "string" && /^[\[{]/.test(value.trim())) {
    try { return JSON.stringify(redact(JSON.parse(value))); } catch { /* Plain diagnostic text. */ }
  }
  return value;
}

export function reportSimulatorConfig(config: SimulatorConfig) {
  return { stationCode: config.stationCode, capacityKWh: config.capacityKWh,
    initialSoc: config.initialSoc, targetSoc: config.targetSoc, powerKW: config.powerKW,
    meterStartWh: config.meterStartWh, intervalSeconds: config.intervalSeconds, speed: config.speed };
}

// Keep recent traffic bounded, and retain lifecycle/errors separately when frequent
// meter messages displace the start. This journal survives reconnects in this tab.
export class SimulatorTestJournal {
  private logs: SimulatorLog[] = [];
  private lifecycle: SimulatorLog[] = [];
  private total = 0;
  private lifecycleTotal = 0;

  constructor(private limit = 5000, private lifecycleLimit = 300) {
    if (!Number.isSafeInteger(limit) || limit < 1 || !Number.isSafeInteger(lifecycleLimit) || lifecycleLimit < 1)
      throw new RangeError("Invalid test journal limits.");
  }

  record(entry: SimulatorLog) {
    const log = { ...entry, detail: String(redact(entry.detail)) };
    this.total++;
    this.logs.push(log);
    if (this.logs.length > this.limit) this.logs.shift();
    let rejected = false;
    try { rejected = JSON.parse(log.detail)?.[0] === 4; } catch { /* System log. */ }
    if (log.direction === "SYS" || rejected || !["MeterValues", "DataTransfer", "Heartbeat"].includes(log.action)) {
      this.lifecycleTotal++;
      this.lifecycle.push(log);
      if (this.lifecycle.length > this.lifecycleLimit) this.lifecycle.shift();
    }
    return log;
  }

  snapshot() {
    return { totalEntries: this.total, omittedTrafficEntries: this.total - this.logs.length,
      omittedLifecycleEntries: this.lifecycleTotal - this.lifecycle.length,
      logs: this.logs.map(log => ({ ...log })), lifecycle: this.lifecycle.map(log => ({ ...log })) };
  }
}

type ReportStation = Pick<Station, "id" | "code" | "status" | "version">;
type ReportInput = {
  source: "automatic-simulator" | "charger-view";
  station: ReportStation;
  view?: StationCharging | null;
  snapshotError?: string | null;
  stream?: { connected: boolean; unavailable: boolean };
  simulator?: {
    config: SimulatorConfig | null; state: SimulatorState | null;
    traffic: ReturnType<SimulatorTestJournal["snapshot"]>;
    lastError: string | null; associateWithAccount: boolean;
    existingTransaction: { id: number; finalWh: number } | null;
  };
  exportedAt?: string;
};

export function createChargingTestReport(input: ReportInput) {
  if (input.view && input.view.station.id !== input.station.id
    || input.simulator?.config && input.simulator.config.stationCode !== input.station.code)
    throw new Error("The report contains a different charge point.");
  const view = input.view;
  const simulator = input.simulator;
  return redact({ schemaVersion: 1, exportedAt: input.exportedAt ?? new Date().toISOString(),
    source: input.source, protocol: "OCPP 1.6J", station: {
      id: input.station.id, code: input.station.code, status: input.station.status, version: input.station.version,
    },
    serverSnapshot: view ? {
      stationStatus: view.station.status, stationVersion: view.station.version,
      canConnect: view.canConnect, deviceConnected: view.deviceConnected, inUse: view.inUse,
      connectionRequest: view.connectionRequest, connector: view.connector ?? null,
      session: view.inUse ? null : view.session, diagnostics: view.inUse ? null : view.diagnostics ?? null,
    } : null,
    serverSnapshotError: input.snapshotError ?? null,
    stream: input.stream ?? null,
    simulator: simulator ? { ...simulator, config: simulator.config ? reportSimulatorConfig(simulator.config) : null } : null,
    limits: {
      browserTraffic: "Recorded in this open tab; reloading clears it. Counts identify omitted entries.",
      serverSnapshot: "Latest authorized charging view and diagnostic samples; not the full persisted server log.",
    },
  });
}

export function downloadChargingTestReport(report: unknown, stationCode: string) {
  const blob = new Blob([JSON.stringify(report, null, 2) + "\n"], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `charging-test-${stationCode.replace(/[^A-Za-z0-9_-]/g, "_")}-${new Date().toISOString().replace(/[:.]/g, "-")}.json`;
  document.body.appendChild(link);
  try { link.click(); } finally { link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
}
