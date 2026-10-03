"use client";

import { useEffect, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import type { User } from "../../../../contracts/account";

export function AccountForm({ initial, children }: { initial: User; children?: ReactNode }) {
  const [user, setUser] = useState(initial);
  const [busy, setBusy] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const router = useRouter();
  const sq = user.language === "sq";
  useEffect(() => { setUser(initial); }, [initial]);
  async function send(action: string, payload: object) {
    setBusy(action); setError(""); setMessage("");
    try {
      const response = await fetch("/api/account/" + action, {method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(payload)});
      if (response.status === 401) { router.replace("/"); router.refresh(); return; }
      const data = response.status === 204 ? null : await response.json();
      if (!response.ok) {
        setError(response.status === 412 ? (sq ? "Të dhënat kanë ndryshuar. Rifresko faqen dhe provo përsëri." : "Your account changed. Refresh the page and try again.") :
          data?.code === "incorrect_password" ? (sq ? "Fjalëkalimi aktual nuk është i saktë." : "The current password is incorrect.") :
          data?.code === "invalid_battery_capacity" ? (sq ? "Kapaciteti duhet të jetë 0.01–1000 kWh, me deri në dy shifra pas presjes." : "Capacity must be 0.01–1000 kWh, with up to two decimal places.") :
          response.status === 400 ? (sq ? "Kontrollo fushat dhe provo përsëri." : "Check the fields and try again.") :
          (sq ? "Shërbimi nuk përgjigjet. Provo përsëri." : "The service is unavailable. Please try again."));
        return;
      }
      if (action === "password") { router.replace("/"); router.refresh(); return; }
      setUser(data as User);
      setMessage(sq ? "Ndryshimet u ruajtën." : "Changes saved.");
      // Refresh the header identity and current version after a profile change.
      router.refresh();
    } catch { setError(sq ? "Lidhja dështoi. Ndryshimet nuk u konfirmuan; provo përsëri." : "Connection failed. Changes were not confirmed; please try again."); }
    finally { setBusy(""); }
  }
  function profile(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const capacity = String(data.get("batteryCapacityKWh") ?? "").trim();
    void send("profile", {firstName:data.get("firstName"),lastName:data.get("lastName"),phone:data.get("phone"),
      vehicleMake:data.get("vehicleMake"),vehicleModel:data.get("vehicleModel"),batteryType:data.get("batteryType"),
      batteryCapacityKWh:capacity === "" ? null : Number(capacity),version:user.version});
  }
  function password(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    if (data.get("newPassword") !== data.get("confirmPassword")) {
      setError(sq ? "Fjalëkalimet e reja nuk përputhen." : "The new passwords do not match."); return;
    }
    void send("password", {currentPassword:data.get("currentPassword"),newPassword:data.get("newPassword")});
    form.reset();
  }
  return <div className="account-forms">
    {error && <div role="alert" className="form-error">{error}<button type="button" className="text-button" onClick={() => router.refresh()}>{sq ? "Rifresko" : "Refresh"}</button></div>}
    {message && <p role="status" className="form-success">{message}</p>}
    <form className="settings-card" onSubmit={profile}><h2>{sq ? "Të dhënat personale" : "Personal information"}</h2>
      <fieldset disabled={Boolean(busy)}><div className="field-grid">
        <label>{sq ? "Emri" : "First name"}<input name="firstName" autoComplete="given-name" maxLength={80} required defaultValue={user.firstName}/></label>
        <label>{sq ? "Mbiemri" : "Last name"}<input name="lastName" autoComplete="family-name" maxLength={80} required defaultValue={user.lastName}/></label>
        <label>Email<input type="email" readOnly value={user.email}/></label>
        <label>{sq ? "Telefoni" : "Phone"}<input name="phone" type="tel" autoComplete="tel" maxLength={32} defaultValue={user.phone ?? ""}/></label>
      </div>
      <h3>{sq ? "Makina jote" : "Your vehicle"}</h3>
      <div className="field-grid">
        <label>{sq ? "Marka" : "Make"}<input name="vehicleMake" maxLength={80} defaultValue={user.vehicleMake ?? ""}/></label>
        <label>{sq ? "Modeli" : "Model"}<input name="vehicleModel" maxLength={80} defaultValue={user.vehicleModel ?? ""}/></label>
        <label>{sq ? "Lloji i baterisë" : "Battery type"}<input name="batteryType" maxLength={80} defaultValue={user.batteryType ?? ""}/></label>
        <label>{sq ? "Kapaciteti i përdorshëm i baterisë (kWh)" : "Usable battery capacity (kWh)"}
          <input id="battery-capacity" name="batteryCapacityKWh" type="number" inputMode="decimal" min="0.01" max="1000" step="0.01" defaultValue={user.batteryCapacityKWh ?? ""} aria-describedby="battery-capacity-help"/>
        </label>
      </div>
      <p id="battery-capacity-help">{sq ? "Fushë opsionale. Vendos kapacitetin e përdorshëm nga specifikimet e makinës. E përdorim me përqindjen dhe fuqinë e raportuar për të vlerësuar kohën deri në 100%." : "Optional. Enter the usable capacity from your vehicle specifications. We use it with reported battery percentage and power to estimate the time to 100%."}</p>
      <button className="button-primary">{busy === "profile" ? (sq ? "Duke ruajtur…" : "Saving…") : (sq ? "Ruaj profilin" : "Save profile")}</button></fieldset>
    </form>
    {children}
    <form className="settings-card" onSubmit={password}><h2>{sq ? "Ndrysho fjalëkalimin" : "Change password"}</h2>
      <p>{sq ? "Përdor 12–128 karaktere. Pas ndryshimit do të hysh përsëri në të gjitha pajisjet." : "Use 12–128 characters. You will need to sign in again on all devices."}</p>
      <fieldset disabled={Boolean(busy)}><label>{sq ? "Fjalëkalimi aktual" : "Current password"}<input name="currentPassword" type="password" autoComplete="current-password" required maxLength={128}/></label>
      <div className="field-grid"><label>{sq ? "Fjalëkalimi i ri" : "New password"}<input name="newPassword" type="password" autoComplete="new-password" required minLength={12} maxLength={128}/></label>
      <label>{sq ? "Përsërite fjalëkalimin" : "Repeat password"}<input name="confirmPassword" type="password" autoComplete="new-password" required minLength={12} maxLength={128}/></label></div>
      <button className="button-primary">{sq ? "Ndrysho fjalëkalimin" : "Change password"}</button></fieldset>
    </form>
  </div>;
}
