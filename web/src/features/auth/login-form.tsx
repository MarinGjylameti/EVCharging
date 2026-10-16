"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import type { Language } from "../../../../contracts/account";
import { waitForApi } from "./wait-for-api";

const copy = {
  sq: { eyebrow: "HAPËSIRA E KARIKIMIT", title: "Mirë se u ktheve.", subtitle: "Hyr për të parë pikat, historikun dhe rrjetin e karikimit.", email: "Adresa e email-it", password: "Fjalëkalimi", signIn: "Hyr në platformë", busy: "Duke u lidhur…", note: "Akses për drejtuesit dhe administratorët.", invalid: "Email-i ose fjalëkalimi nuk është i saktë.", unavailable: "Shërbimi nuk është i disponueshëm. Provo përsëri.", limit: "Shumë përpjekje. Provo përsëri pas një minute.", show: "Shfaq fjalëkalimin", hide: "Fshih fjalëkalimin" },
  en: { eyebrow: "CHARGING WORKSPACE", title: "Welcome back.", subtitle: "Sign in to see charging points, history and your network.", email: "Email address", password: "Password", signIn: "Sign in to platform", busy: "Connecting…", note: "Access for drivers and administrators.", invalid: "The email or password is incorrect.", unavailable: "The service is unavailable. Please try again.", limit: "Too many attempts. Try again in a minute.", show: "Show password", hide: "Hide password" }
};

const registrationCopy = {
  sq: { title: "Krijo llogarinë tënde.", subtitle: "Regjistrohu për të gjetur pika dhe për të ndjekur karikimet e tua.", firstName: "Emri", lastName: "Mbiemri", phone: "Numri i celularit", phoneHint: "Përfshi kodin e shtetit, p.sh. +355691234567.", confirm: "Përsërit fjalëkalimin", passwordHint: "Përdor 12–128 karaktere.", create: "Krijo llogarinë", mismatch: "Fjalëkalimet nuk përputhen.", signUp: "Nuk ke llogari? Regjistrohu", signIn: "Ke llogari? Hyr", invalid_email: "Shkruaj një email të vlefshëm.", invalid_name: "Emri dhe mbiemri kërkohen, deri në 80 karaktere.", invalid_phone: "Shkruaj numrin me kodin e shtetit, p.sh. +355691234567.", invalid_password: "Përdor një fjalëkalim me 12–128 karaktere.", email_unavailable: "Ky email është përdorur. Hyr me llogarinë tënde ose përdor një email tjetër." },
  en: { title: "Create your account.", subtitle: "Register to find stations and follow your charging sessions.", firstName: "First name", lastName: "Last name", phone: "Mobile number", phoneHint: "Include the country code, for example +355691234567.", confirm: "Confirm password", passwordHint: "Use 12–128 characters.", create: "Create account", mismatch: "The passwords do not match.", signUp: "No account? Register", signIn: "Already registered? Sign in", invalid_email: "Enter a valid email address.", invalid_name: "First and last names are required, up to 80 characters.", invalid_phone: "Enter the number with its country code, for example +355691234567.", invalid_password: "Use a password between 12 and 128 characters.", email_unavailable: "This email is already used. Sign in or use another email address." },
};

export function LoginForm({ register = false }: { register?: boolean }) {
  const [language, setLanguage] = useState<Language>("sq");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [visible, setVisible] = useState(false);
  const [waiting, setWaiting] = useState(false);
  const operation = useRef<AbortController | null>(null);
  useEffect(() => () => operation.current?.abort(), []);
  const router = useRouter();
  const t = copy[language];
  const registration = registrationCopy[language];
  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (operation.current) return;
    const values = new FormData(event.currentTarget);
    if (register && values.get("password") !== values.get("confirmPassword")) {
      setError(registration.mismatch);
      return;
    }
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
      const credentials = { email: String(values.get("email") ?? "").trim(), password: values.get("password") };
      const payload = register ? { ...credentials, firstName: values.get("firstName"), lastName: values.get("lastName"), phone: values.get("phone"), language } : credentials;
      const response = await fetch(register ? "/api/auth/register" : "/api/auth/login", { method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload), signal: controller.signal });
      if (!response.ok) {
        const failure = await response.json().catch(() => ({})) as { code?: string };
        const errors: Record<string, string> = { invalid_email: registration.invalid_email, invalid_name: registration.invalid_name, invalid_phone: registration.invalid_phone, invalid_password: registration.invalid_password, email_unavailable: registration.email_unavailable, duplicate_resource: registration.email_unavailable };
        setError(response.status === 429 ? t.limit : register ? errors[failure.code ?? ""] ?? t.unavailable : response.status === 401 ? t.invalid : t.unavailable);
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
  return <div className={register ? "auth-content auth-register" : "auth-content"} lang={language}>
    <div className="language-switch" aria-label="Language">
      {(["sq", "en"] as const).map(value => <button type="button" key={value} aria-pressed={language === value} onClick={() => { setLanguage(value); setError(""); }}>{value === "sq" ? "SQ" : "EN"}</button>)}
    </div>
    <div className="form-intro"><span className="eyebrow">{t.eyebrow}</span><h1>{register ? registration.title : t.title}</h1><p>{register ? registration.subtitle : t.subtitle}</p></div>
    <form onSubmit={submit}>
      {register ? <div className="auth-names">
        <div><label htmlFor="firstName">{registration.firstName}</label><input id="firstName" name="firstName" autoComplete="given-name" maxLength={80} required disabled={busy}/></div>
        <div><label htmlFor="lastName">{registration.lastName}</label><input id="lastName" name="lastName" autoComplete="family-name" maxLength={80} required disabled={busy}/></div>
      </div> : null}
      <label htmlFor="email">{t.email}</label>
      <input id="email" name="email" type="email" autoComplete={register ? "email" : "username"} placeholder="emri@kompania.al" maxLength={254} required disabled={busy}/>
      {register ? <><label htmlFor="phone">{registration.phone}</label><input id="phone" name="phone" type="tel" autoComplete="tel" placeholder="+355691234567" maxLength={32} aria-describedby="phone-hint" required disabled={busy}/><p id="phone-hint" className="auth-hint">{registration.phoneHint}</p></> : null}
      <label htmlFor="password">{t.password}</label>
      <div className="password-field"><input id="password" name="password" type={visible ? "text" : "password"} autoComplete={register ? "new-password" : "current-password"} minLength={register ? 12 : undefined} maxLength={128} aria-describedby={register ? "password-hint" : undefined} required disabled={busy}/><button type="button" className="show-password" onClick={() => setVisible(!visible)} aria-label={visible ? t.hide : t.show}>{visible ? "◉" : "◎"}</button></div>
      {register ? <><p id="password-hint" className="auth-hint">{registration.passwordHint}</p><label htmlFor="confirmPassword">{registration.confirm}</label><input id="confirmPassword" name="confirmPassword" type={visible ? "text" : "password"} autoComplete="new-password" minLength={12} maxLength={128} required disabled={busy}/></> : null}
      {error ? <p className="form-error" role="alert">{error}</p> : null}
      {waiting && <p className="support-note" role="status">{language === "sq" ? "Po presim që shërbimi të bëhet gati. Ndezja pas pasivitetit mund të zgjasë 1–2 minuta; hyrja vazhdon automatikisht." : "Waiting for the service to become ready. Starting after inactivity can take 1–2 minutes; sign-in will continue automatically."}</p>}
      <button className="button-primary" type="submit" disabled={busy}>{busy ? t.busy : register ? registration.create : t.signIn}<span aria-hidden="true">↗</span></button>
    </form>
    <p className="access-note"><span aria-hidden="true">◇</span>{t.note}</p>
    <p className="auth-link"><Link href={register ? "/" : "/register"}>{register ? registration.signIn : registration.signUp}</Link></p>
  </div>;
}
