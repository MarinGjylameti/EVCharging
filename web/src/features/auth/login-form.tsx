"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import type { Language } from "../../../../contracts/account";
import { waitForApi } from "./wait-for-api";

const copy = {
  sq: { eyebrow: "HAPËSIRA E KARIKIMIT", title: "Mirë se u ktheve.", subtitle: "Hyr për të parë pikat, historikun dhe rrjetin e karikimit.", email: "Adresa e email-it", password: "Fjalëkalimi", signIn: "Hyr në platformë", busy: "Duke u lidhur…", note: "Akses për drejtuesit dhe administratorët.", support: "Për këtë provë përdor llogarinë demo të drejtuesit.", invalid: "Email-i ose fjalëkalimi nuk është i saktë.", unavailable: "Shërbimi nuk është i disponueshëm. Provo përsëri.", limit: "Shumë përpjekje. Provo përsëri pas një minute.", show: "Shfaq fjalëkalimin", hide: "Fshih fjalëkalimin" },
  en: { eyebrow: "CHARGING WORKSPACE", title: "Welcome back.", subtitle: "Sign in to see charging points, history and your network.", email: "Email address", password: "Password", signIn: "Sign in to platform", busy: "Connecting…", note: "Access for drivers and administrators.", support: "Use the driver demo account for this preview.", invalid: "The email or password is incorrect.", unavailable: "The service is unavailable. Please try again.", limit: "Too many attempts. Try again in a minute.", show: "Show password", hide: "Hide password" }
};

export function LoginForm() {
  const [language, setLanguage] = useState<Language>("sq");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [visible, setVisible] = useState(false);
  const [waiting, setWaiting] = useState(false);
  const operation = useRef<AbortController | null>(null);
  useEffect(() => () => operation.current?.abort(), []);
  const router = useRouter();
  const t = copy[language];
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (operation.current) return;
    const values = new FormData(event.currentTarget);
    const controller = new AbortController();
    operation.current = controller;
    setBusy(true); setWaiting(false); setError("");
    try {
      const ready = await waitForApi(controller.signal, () => setWaiting(true));
      if (!ready) {
        setError(language === "sq" ? "Shërbimi nuk u bë gati brenda dy minutash. Provo përsëri pak më vonë." : "The service was not ready within two minutes. Please try again later.");
        return;
      }
      setWaiting(false);
      const response = await fetch("/api/auth/login", { method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: values.get("email"), password: values.get("password") }), signal: controller.signal });
      if (!response.ok) {
        setError(response.status === 429 ? t.limit : response.status === 401 ? t.invalid : t.unavailable);
      } else {
        const payload = await response.json() as {user:{role:string}};
        router.replace(payload.user.role === "DRIVER" ? "/driver" : "/dashboard");
        router.refresh();
      }
    } catch { if (!controller.signal.aborted) setError(t.unavailable); }
    finally {
      operation.current = null;
      if (!controller.signal.aborted) { setBusy(false); setWaiting(false); }
    }
  }
  return <div className="auth-content" lang={language}>
    <div className="language-switch" aria-label="Language">
      {(["sq", "en"] as const).map(value => <button type="button" key={value} aria-pressed={language === value} onClick={() => { setLanguage(value); setError(""); }}>{value === "sq" ? "SQ" : "EN"}</button>)}
    </div>
    <div className="form-intro"><span className="eyebrow">{t.eyebrow}</span><h1>{t.title}</h1><p>{t.subtitle}</p></div>
    <form onSubmit={submit}>
      <label htmlFor="email">{t.email}</label>
      <input id="email" name="email" type="email" autoComplete="username" placeholder="emri@kompania.al" maxLength={254} required disabled={busy}/>
      <label htmlFor="password">{t.password}</label>
      <div className="password-field"><input id="password" name="password" type={visible ? "text" : "password"} autoComplete="current-password" maxLength={128} required disabled={busy}/><button type="button" className="show-password" onClick={() => setVisible(!visible)} aria-label={visible ? t.hide : t.show}>{visible ? "◉" : "◎"}</button></div>
      {error ? <p className="form-error" role="alert">{error}</p> : null}
      {waiting && <p className="support-note" role="status">{language === "sq" ? "Po presim që shërbimi të bëhet gati. Ndezja pas pasivitetit mund të zgjasë 1–2 minuta; hyrja vazhdon automatikisht." : "Waiting for the service to become ready. Starting after inactivity can take 1–2 minutes; sign-in will continue automatically."}</p>}
      <button className="button-primary" type="submit" disabled={busy}>{busy ? t.busy : t.signIn}<span aria-hidden="true">↗</span></button>
    </form>
    <p className="access-note"><span aria-hidden="true">◇</span>{t.note}</p>
    <p className="support-note">{t.support}</p>
  </div>;
}
