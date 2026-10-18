export type SimulatorConfig = {
  url: string; stationCode: string; capacityKWh: number; initialSoc: number; targetSoc: number;
  powerKW: number; meterStartWh: number; intervalSeconds: number; speed: number;
};
export type SimulatorState = {
  connected: boolean; status: "Idle" | "Charging" | "Paused" | "Faulted" | "Completed" | "Unavailable" | "Reserved";
  transactionId: number | null; energyKWh: number; soc: number; powerKW: number;
};
export type SimulatorLog = { at: string; direction: "OUT" | "IN" | "SYS"; action: string; detail: string };
export const simulatorErrors = ["ConnectorLockFailure", "EVCommunicationError", "GroundFailure", "HighTemperature", "InternalError", "LocalListConflict", "OtherError", "OverCurrentFailure", "OverVoltage", "PowerMeterFailure", "PowerSwitchFailure", "ReaderFailure", "ResetFailure", "UnderVoltage", "WeakSignal"] as const;
export const simulatorStopReasons = ["Local", "Remote", "EVDisconnected", "EmergencyStop", "DeAuthorized", "HardReset", "PowerLoss", "Reboot", "SoftReset", "UnlockCommand", "Other"] as const;

type Pending = { resolve: (payload: Record<string, unknown>) => void; reject: (error: Error) => void; timer: ReturnType<typeof setTimeout>; action: string };

export function validateSimulatorConfig(config: SimulatorConfig) {
  const url = new URL(config.url);
  if (!["ws:", "wss:"].includes(url.protocol) || url.username || url.password || url.search || url.hash
    || !/^[A-Za-z0-9_-]{1,40}$/.test(config.stationCode)
    || [config.capacityKWh, config.initialSoc, config.targetSoc, config.powerKW, config.meterStartWh, config.intervalSeconds, config.speed].some(value => !Number.isFinite(value))
    || config.capacityKWh <= 0 || config.capacityKWh > 1000
    || config.initialSoc < 0 || config.targetSoc > 100 || config.initialSoc >= config.targetSoc
    || config.powerKW <= 0 || config.powerKW > 1000 || config.meterStartWh < 0 || !Number.isSafeInteger(config.meterStartWh)
    || config.intervalSeconds < 2 || config.intervalSeconds > 60 || config.speed < 1 || config.speed > 60)
    throw new Error("Invalid simulator configuration.");
}

// Uses the same OCPP WebSocket endpoint as a physical charger. Every call waits
// for its matching response; a rejected start never creates a local transaction.
export class AutomaticSimulator {
  private socket: WebSocket | null = null;
  private pending = new Map<string, Pending>();
  private heartbeat: ReturnType<typeof setInterval> | null = null;
  private meter: ReturnType<typeof setInterval> | null = null;
  private lastTick = 0;
  private meterBaseWh: number;
  private busy = false;
  private queue: Promise<void> = Promise.resolve();
  private chargingPowerKW: number;
  private disposed = false;
  private estimatesSupported = true;
  private state: SimulatorState;

  constructor(readonly config: SimulatorConfig, private changed: (state: SimulatorState) => void,
    private logged: (log: SimulatorLog) => void,
    private socketFactory: (url: string, protocol: string) => WebSocket = (url, protocol) => new WebSocket(url, protocol),
    private now: () => number = Date.now) {
    validateSimulatorConfig(config);
    this.meterBaseWh = config.meterStartWh;
    this.chargingPowerKW = config.powerKW;
    this.state = { connected: false, status: "Idle", transactionId: null, energyKWh: 0, soc: config.initialSoc, powerKW: 0 };
  }

  get snapshot() { return { ...this.state }; }
  private timestamp() { return new Date(this.now()).toISOString(); }
  private log(direction: SimulatorLog["direction"], action: string, detail: string) {
    this.logged({ at: this.timestamp(), direction, action, detail });
  }
  private publish() { if (!this.disposed) this.changed(this.snapshot); }
  private clearTimers() {
    if (this.heartbeat) clearInterval(this.heartbeat);
    if (this.meter) clearInterval(this.meter);
    this.heartbeat = this.meter = null;
  }
  private failPending(message: string) {
    for (const call of this.pending.values()) { clearTimeout(call.timer); call.reject(new Error(message)); }
    this.pending.clear();
  }
  private call(action: string, payload: object): Promise<Record<string, unknown>> {
    if (!this.state.connected || !this.socket) return Promise.reject(new Error("Simulator disconnected."));
    const id = crypto.randomUUID();
    const frame = JSON.stringify([2, id, action, payload]);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error(`${action}: response timed out. Reconnect and check the active transaction before retrying.`)); }, 15_000);
      this.pending.set(id, { resolve, reject, timer, action });
      this.log("OUT", action, frame);
      try { this.socket!.send(frame); }
      catch (error) { clearTimeout(timer); this.pending.delete(id); reject(error); }
    });
  }
  private receive(data: unknown) {
    let frame: unknown;
    try { frame = JSON.parse(String(data)); } catch { this.log("SYS", "InvalidMessage", "Response is not JSON."); return; }
    if (!Array.isArray(frame) || typeof frame[1] !== "string") return;
    const pending = this.pending.get(frame[1]);
    if ((frame[0] === 3 || frame[0] === 4) && pending) {
      clearTimeout(pending.timer); this.pending.delete(frame[1]);
      this.log("IN", pending.action, String(data));
      if (frame[0] === 4) pending.reject(new Error(`${pending.action}: ${frame[2]} — ${frame[3]}`));
      else if (frame[2] && typeof frame[2] === "object") pending.resolve(frame[2]);
      else pending.reject(new Error(`${pending.action}: invalid response.`));
    } else if (frame[0] === 2) {
      this.log("IN", String(frame[2]), String(data));
      this.socket?.send(JSON.stringify([4, frame[1], "NotImplemented", "This demo simulator does not implement this server command.", {}]));
    }
  }

  async connect() {
    if (this.disposed) throw new Error("Simulator closed.");
    if (this.state.connected) return;
    if (this.socket) throw new Error("A connection is already in progress.");
    this.log("SYS", "ConnectionAttempt", JSON.stringify({ endpoint: this.config.url, protocol: "ocpp1.6" }));
    const socket = this.socketFactory(this.config.url, "ocpp1.6");
    this.socket = socket;
    try {
      await new Promise<void>((resolve, reject) => {
        const timer = setTimeout(() => { socket.close(); reject(new Error("WebSocket connection timed out.")); }, 15_000);
        socket.onmessage = event => this.receive(event.data);
        socket.onopen = () => { clearTimeout(timer); this.state.connected = true; this.publish(); resolve(); };
        socket.onerror = () => { clearTimeout(timer); reject(new Error("WebSocket connection failed. Check the demo endpoint and anonymous simulator access.")); };
        socket.onclose = () => {
          clearTimeout(timer); reject(new Error("WebSocket connection closed."));
          this.clearTimers(); this.failPending("WebSocket connection closed.");
          if (this.socket === socket) this.socket = null;
          this.state.connected = false; this.state.powerKW = 0;
          if (this.state.transactionId !== null) this.state.status = "Paused";
          this.publish(); this.log("SYS", "Disconnected", this.state.transactionId === null
            ? "Connection closed; no active transaction is tracked by this simulator. Server state was not checked."
            : `Connection closed; transaction ${this.state.transactionId} has no confirmed StopTransaction.`);
        };
      });
      const boot = await this.call("BootNotification", { chargePointVendor: "EVCharging", chargePointModel: "AutomaticDemo", chargePointSerialNumber: this.config.stationCode, firmwareVersion: "1.0.0" });
      if (boot.status !== "Accepted") throw new Error(`BootNotification: ${boot.status}`);
      const interval = typeof boot.interval === "number" && Number.isFinite(boot.interval) ? Math.max(10, Math.min(60, boot.interval)) : 30;
      this.heartbeat = setInterval(() => { void this.call("Heartbeat", {}).catch(error => this.log("SYS", "Heartbeat", String(error))); }, interval * 1000);
      await this.status(this.state.transactionId === null ? "Available" : "SuspendedEV");
      if (this.state.transactionId !== null) this.scheduleMeters();
    } catch (error) { this.disconnect(); throw error; }
  }

  private exclusive(action: () => Promise<void>) {
    const next = this.queue.then(async () => {
      if (this.disposed) throw new Error("Simulator closed.");
      this.busy = true;
      try { await action(); }
      catch (error) { this.disconnect(); throw error; }
      finally { this.busy = false; }
    });
    this.queue = next.catch(() => {});
    return next;
  }
  private status(status: string, errorCode = "NoError") {
    return this.call("StatusNotification", { connectorId: 1, status, errorCode, timestamp: this.timestamp() });
  }
  private advance() {
    const now = this.now();
    if (this.state.status === "Charging") {
      const limit = this.config.capacityKWh * (this.config.targetSoc - this.config.initialSoc) / 100;
      this.state.energyKWh = Math.min(limit, this.state.energyKWh + this.state.powerKW * Math.max(0, now - this.lastTick) / 3_600_000 * this.config.speed);
      this.state.soc = Math.min(this.config.targetSoc, this.config.initialSoc + this.state.energyKWh / this.config.capacityKWh * 100);
    }
    this.lastTick = now;
    this.publish();
  }
  private get meterWh() { return this.meterBaseWh + Math.round(this.state.energyKWh * 1000); }
  private async readings(context = "Sample.Periodic") {
    if (this.state.transactionId === null) return;
    await this.call("MeterValues", { connectorId: 1, transactionId: this.state.transactionId,
      meterValue: [{ timestamp: this.timestamp(), sampledValue: [
        { value: String(this.meterWh), measurand: "Energy.Active.Import.Register", unit: "Wh", context, location: "Outlet" },
        { value: String(this.state.powerKW), measurand: "Power.Active.Import", unit: "kW", context, location: "Outlet" },
        { value: this.state.soc.toFixed(2), measurand: "SoC", unit: "Percent", context, location: "EV" },
      ] }] });
    if (!this.estimatesSupported) return;
    const seconds = this.state.status === "Charging" && this.state.powerKW > 0
      ? Math.ceil(this.config.capacityKWh * Math.max(0, this.config.targetSoc - this.state.soc) / 100 / this.state.powerKW * 3600 / this.config.speed) : 0;
    try {
      const estimate = await this.call("DataTransfer", { vendorId: "EVCharging.Simulator", messageId: "ChargingEstimate",
        data: JSON.stringify({ transactionId: this.state.transactionId, remainingSeconds: seconds <= 86400 ? seconds : 0 }) });
      if (estimate.status !== "Accepted") {
        if (["UnknownVendorId", "UnknownMessageId"].includes(String(estimate.status))) this.estimatesSupported = false;
        this.log("SYS", "ChargingEstimate", "The server did not accept the availability estimate.");
      }
    } catch (error) {
      if (!(error instanceof Error) || !/^DataTransfer: (NotSupported|NotImplemented)\b/.test(error.message)) throw error;
      this.estimatesSupported = false;
      this.log("SYS", "ChargingEstimate", "This server version does not support public estimates yet.");
    }
  }
  private scheduleMeters() {
    if (this.meter) clearInterval(this.meter);
    this.meter = setInterval(() => {
      if (this.busy) return;
      void this.exclusive(async () => {
        if (!this.state.connected || this.state.transactionId === null) return;
        this.advance(); await this.readings();
        if (this.state.status === "Charging" && this.state.soc >= this.config.targetSoc) await this.finish("Local");
      }).catch(error => {
        this.log("SYS", "MeterValues", String(error));
        // A missing response leaves server state uncertain. Preserve transaction
        // identity and energy, disconnect, and allow a deliberate reconnect.
        this.disconnect();
      });
    }, this.config.intervalSeconds * 1000);
  }

  async start() {
    await this.exclusive(async () => {
      if (this.state.transactionId !== null) throw new Error("An active transaction already exists. Stop it first.");
      const auth = await this.call("Authorize", { idTag: "TAG-001" });
      if ((auth.idTagInfo as { status?: string } | undefined)?.status !== "Accepted") throw new Error("Authorization rejected.");
      await this.status("Preparing");
      const meterStart = this.meterWh;
      const start = await this.call("StartTransaction", { connectorId: 1, idTag: "TAG-001", meterStart, timestamp: this.timestamp() });
      if ((start.idTagInfo as { status?: string } | undefined)?.status !== "Accepted" || !Number.isSafeInteger(start.transactionId)) throw new Error("StartTransaction rejected or returned an invalid transaction ID.");
      this.state.transactionId = start.transactionId as number;
      this.meterBaseWh = meterStart;
      this.state.energyKWh = 0; this.state.soc = this.config.initialSoc;
      await this.status("Charging");
      this.state.status = "Charging"; this.state.powerKW = this.chargingPowerKW; this.lastTick = this.now();
      this.publish(); await this.readings("Transaction.Begin"); this.scheduleMeters();
    });
  }
  async pause(byCharger = false) {
    await this.exclusive(async () => {
      if (this.state.status !== "Charging") throw new Error("The simulator is not charging.");
      this.advance(); await this.readings();
      await this.status(byCharger ? "SuspendedEVSE" : "SuspendedEV");
      this.state.status = "Paused"; this.state.powerKW = 0; this.publish(); await this.readings();
    });
  }
  async resume() {
    await this.exclusive(async () => {
      if (this.state.transactionId === null) throw new Error("No active transaction.");
      await this.status("Charging");
      this.state.status = "Charging"; this.state.powerKW = this.chargingPowerKW; this.lastTick = this.now();
      this.publish(); await this.readings(); this.scheduleMeters();
    });
  }
  async fault(errorCode: string) {
    if (!simulatorErrors.some(code => code === errorCode)) throw new Error("Invalid OCPP error code.");
    await this.exclusive(async () => {
      this.advance(); if (this.state.transactionId !== null) await this.readings();
      await this.status("Faulted", errorCode);
      this.state.status = "Faulted"; this.state.powerKW = 0; this.publish(); await this.readings();
    });
  }
  async reportStatus(status: "Available" | "Unavailable" | "Reserved") {
    await this.exclusive(async () => {
      if (this.state.transactionId !== null) throw new Error("Stop the active transaction before changing availability.");
      await this.status(status);
      this.state.status = status === "Available" ? "Idle" : status; this.publish();
    });
  }
  private async finish(reason: string) {
    if (this.state.transactionId === null) throw new Error("No active transaction.");
    this.advance(); this.state.status = "Paused"; this.state.powerKW = 0;
    await this.readings("Transaction.End"); await this.status("Finishing");
    await this.call("StopTransaction", { transactionId: this.state.transactionId, meterStop: this.meterWh, timestamp: this.timestamp(), reason });
    this.state.transactionId = null; this.state.status = "Completed";
    if (this.meter) clearInterval(this.meter); this.meter = null;
    this.publish(); await this.status("Available");
  }
  async stop(reason = "Local") {
    if (!simulatorStopReasons.some(value => value === reason)) throw new Error("Invalid stop reason.");
    await this.exclusive(() => this.finish(reason));
  }
  async setPower(powerKW: number) {
    if (!Number.isFinite(powerKW) || powerKW < 0 || powerKW > 1000) throw new Error("Invalid power.");
    await this.exclusive(async () => {
      this.advance(); this.chargingPowerKW = powerKW;
      if (this.state.status === "Charging") this.state.powerKW = powerKW;
      this.publish(); await this.readings();
    });
  }
  async stopExisting(transactionId: number, meterStop: number) {
    if (!Number.isSafeInteger(transactionId) || !Number.isSafeInteger(meterStop) || meterStop < 0)
      throw new Error("Invalid transaction or final meter reading.");
    await this.exclusive(async () => {
      if (this.state.transactionId !== null) throw new Error("Stop this simulator's active transaction first.");
      await this.call("StopTransaction", { transactionId, meterStop, timestamp: this.timestamp(), reason: "Local" });
      await this.status("Available");
      this.meterBaseWh = meterStop; this.state.energyKWh = 0; this.state.status = "Idle"; this.publish();
    });
  }
  disconnect() {
    this.clearTimers(); this.socket?.close();
    this.state.connected = false; this.state.powerKW = 0;
    if (this.state.transactionId !== null) this.state.status = "Paused";
    this.publish();
  }
  dispose() { this.disposed = true; this.disconnect(); this.failPending("Simulator closed."); }
}
