"use client";

import { useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import type { Language, User } from "../../../../contracts/account";

export function UserMenu({initial}: {initial: User}) {
  const router = useRouter();
  const [user, setUser] = useState(initial);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const firstOptionRef = useRef<HTMLButtonElement>(null);
  const sq = user.language === "sq";
  const name = `${user.firstName} ${user.lastName}`.trim() || user.email;
  const role = user.role === "DRIVER" ? (sq ? "Drejtues" : "Driver") :
    user.role === "SUPERADMIN" ? "Superadmin" : (sq ? "Administrator" : "Administrator");

  useEffect(() => { setUser(initial); }, [initial]);
  useEffect(() => {
    if (!open) return;
    firstOptionRef.current?.focus();
    function outside(event: PointerEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    }
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, [open]);

  async function selectLanguage(language: Language) {
    if (busy || language === user.language) return;
    setBusy(true); setError("");
    try {
      const response = await fetch("/api/account/settings", {method:"POST", headers:{"Content-Type":"application/json"},
        body:JSON.stringify({language, pushEnabled:user.pushEnabled, version:user.version})});
      if (response.status === 401) { window.location.assign("/"); return; }
      if (!response.ok) {
        if (response.status === 412) router.refresh();
        throw new Error();
      }
      setUser(await response.json() as User);
      setOpen(false);
      router.refresh();
    } catch { setError(sq ? "Gjuha nuk u ruajt. Provo përsëri." : "Language could not be saved. Please retry."); }
    finally { setBusy(false); }
  }

  async function signOut() {
    if (busy) return;
    setBusy(true); setError("");
    try {
      const response = await fetch("/api/auth/logout", {method:"POST"});
      if (!response.ok) throw new Error();
      window.location.assign("/");
    } catch { setError(sq ? "Dalja dështoi. Provo përsëri." : "Sign out failed. Please retry."); setBusy(false); }
  }

  return <div className="user-menu" ref={rootRef} onKeyDown={event=>{
    if (event.key === "Escape" && open) { event.preventDefault(); setOpen(false); triggerRef.current?.focus(); }
  }}>
    <button type="button" className="user-menu-trigger" ref={triggerRef} aria-expanded={open}
      aria-controls="user-menu-panel" onClick={()=>{setOpen(value=>!value);setError("");}}>
      <span className="user-menu-avatar" aria-hidden="true">{user.firstName.slice(0,1)}{user.lastName.slice(0,1)}</span>
      <span className="user-menu-identity"><strong>{name}</strong><small>{role}</small></span>
      <span className="user-menu-chevron" aria-hidden="true">{open ? "⌃" : "⌄"}</span>
    </button>
    {open&&<div className="user-menu-panel" id="user-menu-panel" aria-label={sq?"Opsionet e llogarisë":"Account options"}>
      <div className="user-menu-person"><strong>{name}</strong><small>{user.email}</small></div>
      <div className="user-menu-language"><span>{sq?"Gjuha":"Language"}</span>
        <div className="user-menu-language-options">
          <button type="button" ref={firstOptionRef} aria-pressed={user.language==="sq"} disabled={busy}
            onClick={()=>void selectLanguage("sq")}>Shqip</button>
          <button type="button" aria-pressed={user.language==="en"} disabled={busy}
            onClick={()=>void selectLanguage("en")}>English</button>
        </div>
      </div>
      <button type="button" className="user-menu-signout" disabled={busy} onClick={()=>void signOut()}>
        {busy?"…":sq?"Dil nga llogaria":"Sign out"}
      </button>
      {error&&<p className="user-menu-error" role="alert">{error}</p>}
    </div>}
  </div>;
}
