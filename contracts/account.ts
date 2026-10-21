export type Language = "sq" | "en";
export type Role = "DRIVER" | "ADMIN" | "SUPERADMIN";
export type User = {
  id: string; email: string; firstName: string; lastName: string;
  role: Role; language: Language; pushEnabled: boolean; version: number;
  phone: string | null; vehicleMake: string | null; vehicleModel: string | null; batteryType: string | null;
  batteryCapacityKWh: number | null;
};
export type AuthResult = { accessToken: string; expiresAt: string; user: User };
export type ApiFailure = { status?: number; code: string; title?: string; traceId?: string };
export type Notification = { id: string; kind: string; createdAt: string; text: { title: string; body: string } };
export type Station = {
  id: string; code: string; name: string; city: string; address: string;
  latitude: number; longitude: number; connectorType: string; connectorCount: number;
  powerKW: number; pricePerKWh: number; currency: string;
  status: "Available" | "Occupied" | "Charging" | "OutOfService" | "Maintenance" | "Unknown";
  statusSource?: string;
  warningCode?: string | null;
  statusObservedAt?: string | null;
  statusReceivedAt?: string | null;
  availabilityForecast?: {
    connectorId: number; estimatedAvailableAt: string; observedAt: string;
    source: "Device" | "Demo" | "Calculated" | "Simulator"; validUntil?: string | null;
  } | null;
  isDemo: boolean; isArchived?: boolean; version: number;
  locationId?: string | null;
};
export type ChargingHistory = {
  id: string; startedAt: string; endedAt: string; energyKWh: number;
  unitPriceSnapshot: number; totalCost: number; currency: string;
  isDemo: boolean; stationId: string; stationCode: string; stationName: string; city: string;
};
export type DriverHistoryPage = {
  items: ChargingHistory[]; total: number; page: number; pageSize: number; pages: number; offset: number;
  bounds: { from: string; until: string } | null; range: { from: string; until: string } | null;
  summary: { charges: number; energyKWh: number; costs: Record<string, number> };
  chart: { granularity: "day" | "week" | "month"; buckets: { key: string; date: string; from: string; until: string; charges: number; energyKWh: number; costs: Record<string, number> }[] };
  stations: { stationId: string; stationName: string; stationCode: string }[]; currencies: string[]; hasDemo: boolean;
};
export type DriverChargingSession = {
  id: string; stationId: string; stationName: string; stationCode: string;
  connectorId: number; status: string; energyKWh: number | null; powerKW: number | null;
  stateOfCharge: number | null; temperatureCelsius: number | null;
  startedAt: string; stoppedAt: string | null; lastMeasurementAt: string | null;
  cost: number | null; currency: string; isDemo: boolean;
  startReceivedAt?: string; stopReceivedAt?: string | null; lastMeasurementReceivedAt?: string | null;
  deviceClockSkewed?: boolean; energyScope?: string | null; powerScope?: string | null;
  unitPricePerKWh?: number | null;
  powerSource?: "Reported" | null; powerValidUntil?: string | null; version?: number;
  faultCode?: string | null; faultInfo?: string | null;
  batteryCapacityKWh?: number | null; stateOfChargeValidUntil?: string | null;
  readings?: ChargingSampleDiagnostic[]; stopReason?: string | null; completionWarning?: string | null;
};
export type ChargingSampleDiagnostic = {
  measurand: string; value: string; unit: string | null; phase: string | null;
  location: string | null; context: string | null; format: string | null;
  observedAt: string; receivedAt: string;
};
export type ChargingCommandResult = {
  action: string; accepted: boolean; failureCode: string | null;
  description: string | null; receivedAt: string; version?: number; readings?: ChargingSampleDiagnostic[];
};
export type ChargingDiagnostics = {
  externalSessionId: string | null; meterStartWh: number | null; stopReason: string | null;
  lastCommand: ChargingCommandResult | null; lastFailure?: ChargingCommandResult | null;
  samples: ChargingSampleDiagnostic[];
};
export type DriverConnectorState = {
  number?: number;
  status: string; faultCode: string | null; faultInfo: string | null; version?: number;
  deviceStatus?: string | null; deviceFaultCode?: string | null; deviceFaultInfo?: string | null;
};
export type StationCharging = {
  driverAccessMode?: "Demo" | "Card" | null;
  station: Station; canConnect: boolean; deviceConnected: boolean; inUse: boolean;
  connectionRequest: { id: string; state: string; expiresAt: string } | null;
  session: DriverChargingSession | null;
  connector?: DriverConnectorState | null;
  diagnostics?: ChargingDiagnostics | null;
  blockingConnection?: {
    stationId: string; stationName: string; connectorId: number;
    state: "Waiting" | "Linked"; expiresAt: string;
  } | null;
};
export type DriverChargingState = { canTestSimulator: boolean; sessions: DriverChargingSession[] };
export type DriverLiveSnapshot = {
  stations: Station[]; history: ChargingHistory[];
};
export type ChargingRealtimeEvent = {
  eventId: string; eventType: string; entityType: string; entityId: string;
  entityVersion: number; occurredAt: string; stationId: string; version: number;
  station?: Station | null; session?: DriverChargingSession | null;
  connector?: DriverConnectorState | null;
  command?: ChargingCommandResult | null;
};
