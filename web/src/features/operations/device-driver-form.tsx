"use client";
import { useEffect, useState } from "react";
import { useTestMode } from "../../components/runtime-features";
import type { Device, Driver } from "../../../../contracts/operations";
import { failure, mutate, operations } from "./api";

export function DeviceDriverForm({ device, stationId, sq, disabled, onSaved }: {
  device: Device; stationId: string; sq: boolean; disabled: boolean; onSaved: (device: Device) => void;
}) {
  const testMode = useTestMode();
  const [drivers, setDrivers] = useState<Driver[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [driverId, setDriverId] = useState(device.assignedDriverId ?? "");
  const [cardInput, setCardInput] = useState(device.cardIdTag ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState(false);
  useEffect(() => {
    setDriverId(device.assignedDriverId ?? "");
    setCardInput(device.cardIdTag ?? "");
  }, [device.assignedDriverId, device.cardIdTag]);
  useEffect(() => {
    const controller = new AbortController();
    void operations<Driver[]>("drivers", controller.signal).then(rows => {
      if (!controller.signal.aborted) { setDrivers(rows.filter(x => x.isActive)); setLoaded(true); }
    }).catch(e => { if (!controller.signal.aborted) setError(failure(e, sq)); });
    return () => controller.abort();
  }, [sq]);
  async function save() {
    if (busy || disabled || !loaded) return;
    setBusy(true); setError(""); setSaved(false);
    try {
      const result = await mutate<Device>("device", "assignDriver", {
        version: device.version, driverId: driverId || null, cardInput: driverId ? cardInput : "",
      }, stationId);
      onSaved(result); setCardInput(result.cardIdTag ?? ""); setSaved(true);
    } catch (e) { setError(failure(e, sq)); }
    finally { setBusy(false); }
  }
  return <form onSubmit={e => { e.preventDefault(); void save(); }}>
    <h4>{sq ? "Kush i sheh të dhënat e karikuesit?" : "Who sees the charger data?"}</h4>
    {error && <p className="form-error" role="alert">{error}</p>}
    {saved && <p role="status">{sq ? "Caktimi u ruajt." : "Assignment saved."}</p>}
    <fieldset className="station-form-fields" disabled={disabled || busy || !loaded}>
      <label>{sq ? "Përdoruesi Driver" : "Driver account"}<select value={driverId} onChange={e => { setDriverId(e.target.value); setSaved(false); }}>
        <option value="">{sq ? "Pa caktim automatik" : "No automatic assignment"}</option>
        {driverId && !drivers.some(x => x.id === driverId) && <option value={driverId}>{sq ? "Përdoruesi aktual (joaktiv)" : "Current account (inactive)"}</option>}
        {drivers.map(driver => <option key={driver.id} value={driver.id}>{driver.firstName} {driver.lastName} · {driver.email}</option>)}
      </select></label>
      <label>{testMode ? (sq ? "Kodi i kartës / JSON (opsional)" : "Card code / JSON (optional)") : (sq ? "Kodi i kartës" : "Card code")}<textarea required={!testMode && Boolean(driverId)} disabled={!driverId} rows={testMode ? 3 : 1} maxLength={4096} value={cardInput}
        onChange={e => { setCardInput(e.target.value); setSaved(false); }} placeholder="CARD-001"/></label>
      <p>{!driverId ? (sq ? "Zgjidh përdoruesin të cilit i përket karta." : "Choose the account that owns the card.")
        : cardInput.trim() ? (sq ? "Vetëm karikimet me këtë kartë do t'i caktohen këtij përdoruesi. Karikimet me karta të tjera të lejuara ruhen pa iu shfaqur këtij përdoruesi." : "Only charges using this card will be assigned to this account. Charges using other allowed cards are saved without being shown to this account.")
        : !testMode ? (sq ? "Kërkohet karta për karikime të reja. Caktimet e mëparshme pa kartë janë joaktive." : "A card is required for new charges. Previous assignments without a card are inactive.")
        : (sq ? "Demonstrim: sinjalet e pajisjes dhe të gjitha karikimet e reja i shfaqen këtij përdoruesi, pa filtruar kartën." : "Demonstration: device signals and every new charge are shown to this account, without filtering the card.")}</p>
      {testMode && <small>{sq ? "Mund të ngjitësh kodin, JSON me idTag ose mesazhin e plotë Authorize/StartTransaction. Ruhet vetëm idTag. Caktimi ndryshohet kur nuk ka karikim aktiv; historiku ruan përdoruesin fillestar." : "Paste the code, JSON containing idTag, or a full Authorize/StartTransaction message. Only idTag is saved. Change the assignment when no charge is active; history keeps its original owner."}</small>}
      <button className="button-primary" disabled={busy || !loaded}>{busy ? (sq ? "Duke ruajtur…" : "Saving…") : (sq ? "Ruaj caktimin" : "Save assignment")}</button>
    </fieldset>
  </form>;
}
