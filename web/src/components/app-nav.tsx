"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useId, useRef, useState } from "react";
import type { Language } from "../../../contracts/account";

type Area = "driver" | "dashboard";

const links = {
  driver: [
    {href:"/driver", icon:"⌖", sq:"Harta e pikave", en:"Charging map", shortSq:"Harta", shortEn:"Map"},
    {href:"/driver/history", icon:"◷", sq:"Historiku im", en:"My history", shortSq:"Historiku", shortEn:"History"},
    {href:"/driver/account", icon:"◇", sq:"Profili dhe makina", en:"Profile and vehicle", shortSq:"Profili", shortEn:"Profile"}
  ],
  dashboard: [
    {href:"/dashboard", icon:"▦", sq:"Përmbledhje", en:"Overview", shortSq:"Paneli", shortEn:"Home"},
    {href:"/dashboard/stations", icon:"⌖", sq:"Pikat", en:"Stations", shortSq:"Pikat", shortEn:"Stations"},
    {href:"/dashboard/locations",icon:"⌂",sq:"Vendndodhjet",en:"Locations",shortSq:"Vendet",shortEn:"Locations"},
    {href:"/dashboard/sessions",icon:"ϟ",sq:"Karikimet",en:"Sessions",shortSq:"Karikimet",shortEn:"Sessions"},
    {href:"/dashboard/prices",icon:"€",sq:"Historiku i çmimeve",en:"Price history",shortSq:"Çmimet",shortEn:"Prices"},
    {href:"/dashboard/audit",icon:"≡",sq:"Aktiviteti dhe auditimi",en:"Activity and audit",shortSq:"Audit",shortEn:"Audit"},
    {href:"/dashboard/notifications", icon:"◷", sq:"Njoftimet", en:"Notifications", shortSq:"Njoftime", shortEn:"Alerts"},
    {href:"/dashboard/account", icon:"◇", sq:"Llogaria", en:"Account", shortSq:"Llogaria", shortEn:"Account"}
  ]
};

const adminLinks = [
  {href:"/dashboard/administrators",icon:"◇",sq:"Administratorët",en:"Administrators",shortSq:"Adminët",shortEn:"Admins"},
  {href:"/dashboard/logs",icon:"≡",sq:"Loget e lidhjes",en:"Connection logs",shortSq:"Loget",shortEn:"Logs"},
  {href:"/dashboard/drivers",icon:"♙",sq:"Drejtuesit",en:"Drivers",shortSq:"Drejtuesit",shortEn:"Drivers"}
];
const mobilePrimary = new Set(["/dashboard", "/dashboard/stations", "/dashboard/sessions", "/dashboard/notifications"]);
const phoneMedia = "(max-width: 700px), (max-width: 1000px) and (max-height: 500px) and (pointer: coarse)";

function MobileIcon({href}: {href: string}) {
  const section = href.split("/").at(-1);
  const paths: Record<string, React.ReactNode> = {
    dashboard: <><rect x="3" y="3" width="7" height="7" rx="1.5"/><rect x="14" y="3" width="7" height="7" rx="1.5"/><rect x="3" y="14" width="7" height="7" rx="1.5"/><rect x="14" y="14" width="7" height="7" rx="1.5"/></>,
    stations: <><rect x="4" y="3" width="10" height="18" rx="2"/><path d="M7 7h4M14 11h2l3 3v4a2 2 0 0 0 2-2V8l-3-3M9 11l-2 4h4l-2 4"/></>,
    sessions: <path d="m13 2-9 12h7l-1 8 10-13h-7l1-7Z"/>,
    notifications: <><path d="M5 17h14l-2-3V9a5 5 0 0 0-10 0v5l-2 3ZM10 21h4M12 2v2"/></>,
    locations: <><path d="M20 10c0 6-8 12-8 12S4 16 4 10a8 8 0 1 1 16 0Z"/><circle cx="12" cy="10" r="2.5"/></>,
    prices: <><path d="M17 5a6 6 0 0 0-10 4v6a6 6 0 0 0 10 4M4 10h10M4 14h9"/></>,
    audit: <><path d="M8 3H5v18h14V3h-3M8 10h8M8 14h8M8 18h5"/><rect x="8" y="2" width="8" height="4" rx="1"/></>,
    account: <><circle cx="12" cy="8" r="4"/><path d="M4 21v-2a8 8 0 0 1 16 0v2"/></>,
    administrators: <><path d="M12 2 3 6v6c0 5 9 10 9 10s9-5 9-10V6l-9-4Z"/><path d="m8 12 3 3 5-6"/></>,
    logs: <><path d="M6 3h12v18H6ZM9 7h6M9 11h6M9 15h3"/></>,
    drivers: <><circle cx="9" cy="7" r="3"/><path d="M3 21v-3a6 6 0 0 1 12 0v3M16 4a3 3 0 0 1 0 6M18 13a5 5 0 0 1 3 5v3"/></>
  };
  return <svg className="app-nav-mobile-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"
    strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[section ?? ""]}</svg>;
}

export function AppNav({area, language, superadmin=false}: {area: Area; language: Language;superadmin?:boolean}) {
  const pathname = usePathname();
  const sq = language === "sq";
  const items = [...links[area], ...(area === "dashboard" && superadmin ? adminLinks : [])];
  const isActive = (href: string) => pathname === href || (pathname.startsWith(`${href}/`) && href !== `/${area}`);
  const secondary = items.filter(item => !mobilePrimary.has(item.href));
  const activeSecondary = secondary.find(item => isActive(item.href));
  const dialogRef = useRef<HTMLDialogElement>(null);
  const dialogId = useId();
  const [menuPath, setMenuPath] = useState<string | null>(null);
  const open = menuPath === pathname;

  useEffect(() => {
    if (!open) return;
    const dialog = dialogRef.current;
    if (!dialog) return;
    const mobile = window.matchMedia(phoneMedia);
    if (!mobile.matches) { setMenuPath(null); return; }
    dialog.showModal();
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    const closeOnDesktop = () => { if (!mobile.matches) setMenuPath(null); };
    mobile.addEventListener("change", closeOnDesktop);
    return () => {
      mobile.removeEventListener("change", closeOnDesktop);
      document.body.style.overflow = previousOverflow;
      dialog.close();
    };
  }, [open]);

  return <><nav className={area === "dashboard" ? "workspace-nav" : "driver-nav"}
    aria-label={sq ? "Menuja kryesore" : "Main navigation"}>
    {items.map(item => {
      const active = isActive(item.href);
      const label = sq ? item.sq : item.en;
      return <Link key={item.href} href={item.href} className={area === "dashboard" && !mobilePrimary.has(item.href) ? "app-nav-secondary" : undefined}
        aria-current={active ? "page" : undefined} aria-label={label}>
        <span className="app-nav-icon" aria-hidden="true">{item.icon}</span>
        {area === "dashboard" && <MobileIcon href={item.href}/>}
        <span className="app-nav-label app-nav-label-full">{label}</span>
        <span className="app-nav-label app-nav-label-short" aria-hidden="true">{sq ? item.shortSq : item.shortEn}</span>
      </Link>;
    })}
    {area === "dashboard" && <button type="button" className="app-nav-more" aria-expanded={open}
      aria-controls={dialogId} aria-haspopup="dialog" data-active={Boolean(activeSecondary)}
      aria-label={activeSecondary ? `${sq ? "Më shumë" : "More"}: ${sq ? activeSecondary.sq : activeSecondary.en}` : undefined}
      onClick={() => setMenuPath(pathname)}>
      <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><circle cx="5" cy="12" r="2"/><circle cx="12" cy="12" r="2"/><circle cx="19" cy="12" r="2"/></svg>
      <span>{sq ? "Më shumë" : "More"}</span>
    </button>}
  </nav>
  {area === "dashboard" && <dialog ref={dialogRef} id={dialogId} className="app-nav-sheet" aria-labelledby={`${dialogId}-title`}
    onClose={() => setMenuPath(null)} onCancel={() => setMenuPath(null)} onClick={event => {
      if (event.target !== event.currentTarget) return;
      const bounds = event.currentTarget.getBoundingClientRect();
      if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) setMenuPath(null);
    }}>
    <div className="app-nav-sheet-heading"><h2 id={`${dialogId}-title`}>{sq ? "Më shumë" : "More"}</h2>
      <button type="button" autoFocus aria-label={sq ? "Mbyll menunë" : "Close menu"} onClick={() => setMenuPath(null)}>×</button></div>
    <nav aria-label={sq ? "Seksionet e tjera" : "More sections"}>
      {secondary.map(item => <Link key={item.href} href={item.href} aria-current={isActive(item.href) ? "page" : undefined} onClick={() => setMenuPath(null)}>
        <MobileIcon href={item.href}/><span>{sq ? item.sq : item.en}</span><span aria-hidden="true">{isActive(item.href) ? "✓" : "›"}</span>
      </Link>)}
    </nav>
  </dialog>}</>;
}
