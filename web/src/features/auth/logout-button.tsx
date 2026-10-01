"use client";
import { useState } from "react";
export function LogoutButton({ language }: { language: string }) {
  const [error, setError] = useState(false);
  const [busy, setBusy] = useState(false);
  return <div><button className="button-outline" disabled={busy} onClick={async () => {
    setBusy(true); setError(false);
    try {
      const response = await fetch("/api/auth/logout", { method: "POST" });
      if (!response.ok) throw new Error();
      window.location.assign("/");
    } catch { setError(true); setBusy(false); }
  }}>{busy ? "…" : language === "sq" ? "Dil nga llogaria" : "Sign out"}</button>{error ? <p role="alert">{language === "sq" ? "Dalja dështoi. Provo përsëri." : "Sign out failed. Try again."}</p> : null}</div>;
}
