export type Language = "sq" | "en";
export type Role = "DRIVER" | "ADMIN" | "SUPERADMIN";
export type User = {
  id: string; email: string; firstName: string; lastName: string;
  role: Role; language: Language; pushEnabled: boolean; version: number;
  phone: string | null; vehicleMake: string | null; vehicleModel: string | null; batteryType: string | null;
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
  statusObservedAt?: string | null;
  statusReceivedAt?: string | null;
  availabilityForecast?: {
    connectorId: number; estimatedAvailableAt: string; observedAt: string;
    source: "Device" | "Demo";
  } | null;
  isDemo: boolean; isArchived?: boolean; version: number;
};
export type ChargingHistory = {
  id: string; startedAt: string; endedAt: string; energyKWh: number;
  unitPriceSnapshot: number; totalCost: number; currency: string;
  isDemo: boolean; stationId: string; stationCode: string; stationName: string; city: string;
};
export type DriverChargingSession = {
  id: string; stationId: string; stationName: string; stationCode: string;
  connectorId: number; status: string; energyKWh: number; powerKW: number | null;
  stateOfCharge: number | null; temperatureCelsius: number | null;
  startedAt: string; stoppedAt: string | null; lastMeasurementAt: string | null;
  cost: number | null; currency: string; isDemo: boolean;
};
export type DriverChargingState = { canTestSimulator: boolean; sessions: DriverChargingSession[] };
export type DriverLiveSnapshot = {
  stations: Station[]; history: ChargingHistory[]; chargingState: DriverChargingState;
};
