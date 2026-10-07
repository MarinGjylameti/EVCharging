import { randomUUID } from 'node:crypto';
import WebSocket from 'ws';

// Mirrors the existing backend test helper: [2, callId, action, payload], matched
// CALLRESULT/CALLERROR. This is a seven-message fixture, not a simulator service.
export class OcppTestClient {
  constructor(settings) {
    this.settings = settings;
    this.pending = new Map();
    this.transactionId = null;
    this.ownsTransaction = false;
    this.lastRegisterWh = 1000;
  }

  async connect() {
    const url = new URL(`/ws/cs/${encodeURIComponent(this.settings.identity)}`, this.settings.apiUrl);
    url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    this.socket = new WebSocket(url, 'ocpp1.6', {
      headers: { Authorization: `Basic ${Buffer.from(`${this.settings.identity}:${this.settings.chargerPassword}`).toString('base64')}` },
      handshakeTimeout: 10_000, maxPayload: 65_536, followRedirects: false
    });
    this.socket.on('error', () => this.failPending('OCPP test transport failed.'));
    this.socket.on('close', () => { clearInterval(this.heartbeatTimer); this.failPending('OCPP test connection closed.'); });
    this.socket.on('message', (raw, binary) => {
      let reply;
      try { reply = JSON.parse(raw.toString()); } catch { this.failPending('Invalid OCPP test reply.'); return; }
      if (binary || !Array.isArray(reply) || ![3, 4].includes(reply[0])) {
        this.failPending('Unexpected OCPP test reply.'); return;
      }
      const call = this.pending.get(reply[1]);
      if (!call) return;
      this.pending.delete(reply[1]); clearTimeout(call.timeout);
      if (reply[0] === 4) call.reject(new Error(`OCPP ${call.action} was rejected (CALLERROR); inspect the dedicated environment logs.`));
      else call.resolve(reply[2]);
    });
    await new Promise((resolve, reject) => {
      this.socket.once('open', resolve);
      this.socket.once('error', () => reject(new Error('OCPP test handshake failed; no credentials or reply contents are printed.')));
      this.socket.once('close', () => reject(new Error('OCPP test connection closed before admission.')));
    });
    const boot = await this.call('BootNotification', { chargePointVendor: 'EVCharging E2E', chargePointModel: 'Repository fixture' });
    if (boot.status !== 'Accepted') throw new Error('OCPP test BootNotification was not accepted.');
    await this.call('Heartbeat', {});
    this.heartbeatTimer = setInterval(() => {
      void this.call('Heartbeat', {}).catch(() => { clearInterval(this.heartbeatTimer); });
    }, 10_000);
  }

  call(action, payload) {
    if (this.socket?.readyState !== WebSocket.OPEN) return Promise.reject(new Error('The E2E OCPP connection is not open.'));
    const id = randomUUID();
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`OCPP ${action} reply timed out; it may already be committed. Inspect the dedicated charger before another run.`));
      }, 10_000);
      this.pending.set(id, { resolve, reject, timeout, action });
      this.socket.send(JSON.stringify([2, id, action, payload]), error => {
        if (error) { clearTimeout(timeout); this.pending.delete(id); reject(new Error(`OCPP ${action} could not be sent.`)); }
      });
    });
  }

  async authorize() {
    const reply = await this.call('Authorize', { idTag: this.settings.tag });
    if (reply.idTagInfo?.status !== 'Accepted') throw new Error('The configured E2E tag was not authorized.');
  }

  status(status) {
    return this.call('StatusNotification', { connectorId: 1, status, errorCode: 'NoError', timestamp: new Date().toISOString() });
  }

  async start(startedAt) {
    if (this.ownsTransaction) throw new Error('This fixture already owns a transaction.');
    const reply = await this.call('StartTransaction', {
      connectorId: 1, idTag: this.settings.tag, meterStart: this.lastRegisterWh, timestamp: startedAt
    });
    if (!Number.isInteger(reply.transactionId) || reply.transactionId < 1) throw new Error('StartTransaction did not return a valid transaction ID. Inspect the reserved charger before another run.');
    this.transactionId = reply.transactionId;
    this.ownsTransaction = true;
    await this.status('Charging');
  }

  meter(energyWh, powerKW = 7.4, soc = 45) {
    if (!this.ownsTransaction || !Number.isInteger(energyWh) || energyWh < 0) throw new Error('Metering requires this fixture’s own active transaction and integral Wh.');
    const register = 1000 + energyWh;
    if (register < this.lastRegisterWh) throw new Error('E2E meter register cannot decrease.');
    this.lastRegisterWh = register; // Retain even if the reply is lost, so cleanup cannot regress the final meter.
    return this.call('MeterValues', {
      connectorId: 1, transactionId: this.transactionId,
      meterValue: [{ timestamp: new Date().toISOString(), sampledValue: [
        { value: String(register), measurand: 'Energy.Active.Import.Register', unit: 'Wh' },
        { value: String(powerKW), measurand: 'Power.Active.Import', unit: 'kW' },
        { value: String(soc), measurand: 'SoC', unit: 'Percent' }
      ] }]
    });
  }

  async stop() {
    if (!this.ownsTransaction) return;
    await this.call('StopTransaction', { transactionId: this.transactionId, meterStop: this.lastRegisterWh, timestamp: new Date().toISOString(), reason: 'Local' });
    this.ownsTransaction = false;
    await this.status('Available');
  }

  failPending(message) {
    for (const call of this.pending.values()) { clearTimeout(call.timeout); call.reject(new Error(message)); }
    this.pending.clear();
  }

  async close() {
    clearInterval(this.heartbeatTimer);
    if (!this.socket || this.socket.readyState === WebSocket.CLOSED) return;
    await new Promise(resolve => {
      const timeout = setTimeout(() => { this.socket.terminate(); resolve(); }, 2000);
      this.socket.once('close', () => { clearTimeout(timeout); resolve(); });
      this.socket.close(1000, 'E2E fixture finished');
    });
  }
}
