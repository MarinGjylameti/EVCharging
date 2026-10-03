"use client";

import { useEffect, useState } from "react";
import type { Language } from "../../../../contracts/account";
import { mapProviderFromPreference, mapProviderStorageKey, type MapProvider } from "@/lib/map-directions";

export function NavigationSettings({language}: {language: Language}) {
  const sq = language === "sq";
  const [provider, setProvider] = useState<MapProvider>("google");
  const [message, setMessage] = useState("");

  useEffect(() => {
    try { setProvider(mapProviderFromPreference(localStorage.getItem(mapProviderStorageKey))); }
    catch { setProvider("google"); }
  }, []);

  function choose(value: MapProvider) {
    try {
      localStorage.setItem(mapProviderStorageKey, value);
      setProvider(value);
      setMessage(sq ? "Zgjedhja u ruajt në këtë shfletues." : "Choice saved in this browser.");
    } catch {
      setMessage(sq ? "Shfletuesi nuk e ruajti zgjedhjen. Provo përsëri." : "Your browser could not save the choice. Please retry.");
    }
  }

  return <section id="map-preferences" className="navigation-settings-card" aria-labelledby="navigation-settings-title">
      <h2 id="navigation-settings-title">{sq ? "Aplikacioni i navigimit" : "Navigation app"}</h2>
      <p>{sq ? "Kur hap udhëzimet nga një pikë karikimi, përdor këtë aplikacion. Google Maps është parazgjedhja." :
        "Use this app when opening directions to a charging station. Google Maps is the default."}</p>
      <div className="navigation-provider-options" role="group" aria-label={sq ? "Zgjidh aplikacionin e navigimit" : "Choose navigation app"}>
        <button type="button" className="navigation-provider-option" aria-pressed={provider === "google"}
          onClick={() => choose("google")}>
          <span className="navigation-provider-icon" aria-hidden="true">G</span>
          <span><strong>Google Maps</strong><small>{sq ? "Parazgjedhja" : "Default"}</small></span>
          <span className="navigation-provider-check" aria-hidden="true">{provider === "google" ? "✓" : ""}</span>
        </button>
        <button type="button" className="navigation-provider-option" aria-pressed={provider === "apple"}
          onClick={() => choose("apple")}>
          <span className="navigation-provider-icon apple" aria-hidden="true">A</span>
          <span><strong>Apple Maps</strong><small>{sq ? "Zgjidhe nëse e përdor në pajisjen tënde" : "Choose if you use it on your device"}</small></span>
          <span className="navigation-provider-check" aria-hidden="true">{provider === "apple" ? "✓" : ""}</span>
        </button>
      </div>
      <p className="navigation-settings-note">{sq ? "Kjo preferencë ruhet në këtë shfletues. Në një pajisje tjetër, parazgjedhja është Google Maps." :
        "This preference is saved in this browser. Google Maps is the default on another device."}</p>
      {message && <p className="navigation-settings-message" role="status">{message}</p>}
  </section>;
}
