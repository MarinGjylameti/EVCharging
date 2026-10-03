import type { Language } from "../../../../contracts/account";

const faults: Record<string, [string, string]> = {
  ConnectorLockFailure: ["Kyçja e lidhësit dështoi", "Connector lock failed"],
  EVCommunicationError: ["Komunikimi me makinën dështoi", "Vehicle communication failed"],
  GroundFailure: ["Defekt në tokëzim", "Ground fault"],
  HighTemperature: ["Temperaturë e lartë", "High temperature"],
  InternalError: ["Gabim i brendshëm i pikës", "Internal charger error"],
  LocalListConflict: ["Konflikt në listën e autorizimit", "Authorization list conflict"],
  OtherError: ["Pika raportoi një gabim", "Charger reported an error"],
  OverCurrentFailure: ["Rrymë mbi kufirin", "Overcurrent detected"],
  OverVoltage: ["Tension mbi kufirin", "Overvoltage detected"],
  PowerMeterFailure: ["Defekt i matësit", "Power meter failed"],
  PowerSwitchFailure: ["Defekt i çelësit të fuqisë", "Power switch failed"],
  ReaderFailure: ["Defekt i lexuesit", "Card reader failed"],
  ResetFailure: ["Rinisja dështoi", "Reset failed"],
  UnderVoltage: ["Tension nën kufirin", "Undervoltage detected"],
  WeakSignal: ["Sinjal i dobët", "Weak signal"],
};

export function chargingFaultLabel(code: string, language: Language) {
  return faults[code]?.[language === "sq" ? 0 : 1] ?? (language === "sq" ? "Pika raportoi një gabim" : "Charger reported an error");
}

export function StationWarning({ code, language }: { code?: string | null; language: Language }) {
  if (!code || code === "NoError") return null;
  return <span className="station-warning-badge" role="status">{language === "sq" ? "Paralajmërim" : "Warning"}: {chargingFaultLabel(code, language)}</span>;
}

export function ChargingFault({ code, info, language, status = "Faulted" }: { code: string; info?: string | null; language: Language; status?: string | null }) {
  if (code === "NoError") return null;
  const warning = status !== "Faulted";
  return <div className={`charge-fault-notice${warning ? " is-warning" : ""}`} role={warning ? "status" : "alert"}>
    <strong>{warning ? `${language === "sq" ? "Paralajmërim" : "Warning"}: ` : ""}{chargingFaultLabel(code, language)}</strong>
    <span>{code}{info ? ` · ${info}` : ""}</span>
  </div>;
}
