"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import type { User } from "../../../../contracts/account";

export function AccountForm({ initial }: { initial: User }) {
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
    void send("profile", {firstName:data.get("firstName"),lastName:data.get("lastName"),phone:data.get("phone"),
      vehicleMake:user.vehicleMake,vehicleModel:user.vehicleModel,batteryType:user.batteryType,version:user.version});
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
      </div><button className="button-primary">{busy === "profile" ? (sq ? "Duke ruajtur…" : "Saving…") : (sq ? "Ruaj profilin" : "Save profile")}</button></fieldset>
    </form>
    <form className="settings-card" onSubmit={password}><h2>{sq ? "Ndrysho fjalëkalimin" : "Change password"}</h2>
      <p>{sq ? "Përdor 12–128 karaktere. Pas ndryshimit do të hysh përsëri në të gjitha pajisjet." : "Use 12–128 characters. You will need to sign in again on all devices."}</p>
      <fieldset disabled={Boolean(busy)}><label>{sq ? "Fjalëkalimi aktual" : "Current password"}<input name="currentPassword" type="password" autoComplete="current-password" required maxLength={128}/></label>
      <div className="field-grid"><label>{sq ? "Fjalëkalimi i ri" : "New password"}<input name="newPassword" type="password" autoComplete="new-password" required minLength={12} maxLength={128}/></label>
      <label>{sq ? "Përsërite fjalëkalimin" : "Repeat password"}<input name="confirmPassword" type="password" autoComplete="new-password" required minLength={12} maxLength={128}/></label></div>
      <button className="button-primary">{sq ? "Ndrysho fjalëkalimin" : "Change password"}</button></fieldset>
    </form>
  </div>;
}
