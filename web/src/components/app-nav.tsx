"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { Language } from "../../../contracts/account";

type Area = "driver" | "dashboard";

const links = {
  driver: [
    {href:"/driver", icon:"⌖", sq:"Harta e pikave", en:"Charging map", shortSq:"Harta", shortEn:"Map"},
    {href:"/driver/history", icon:"◷", sq:"Historiku im", en:"My history", shortSq:"Historiku", shortEn:"History"},
    {href:"/driver/account", icon:"◇", sq:"Profili dhe makina", en:"Profile and vehicle", shortSq:"Profili", shortEn:"Profile"},
    {href:"/driver/settings", icon:"⚙", sq:"Cilësimet", en:"Settings", shortSq:"Cilësime", shortEn:"Settings"}
  ],
  dashboard: [
    {href:"/dashboard", icon:"▦", sq:"Përmbledhje", en:"Overview", shortSq:"Paneli", shortEn:"Home"},
    {href:"/dashboard/stations", icon:"⌖", sq:"Pikat", en:"Stations", shortSq:"Pikat", shortEn:"Stations"},
    {href:"/dashboard/notifications", icon:"◷", sq:"Njoftimet", en:"Notifications", shortSq:"Njoftime", shortEn:"Alerts"},
    {href:"/dashboard/account", icon:"◇", sq:"Llogaria", en:"Account", shortSq:"Llogaria", shortEn:"Account"}
  ]
};

export function AppNav({area, language}: {area: Area; language: Language}) {
  const pathname = usePathname();
  const sq = language === "sq";
  return <nav className={area === "dashboard" ? "workspace-nav" : "driver-nav"}
    aria-label={sq ? "Menuja kryesore" : "Main navigation"}>
    {links[area].map(item => {
      const active = pathname === item.href ||
        (pathname.startsWith(`${item.href}/`) && item.href !== `/${area}`);
      const label = sq ? item.sq : item.en;
      return <Link key={item.href} href={item.href} aria-current={active ? "page" : undefined} aria-label={label}>
        <span className="app-nav-icon" aria-hidden="true">{item.icon}</span>
        <span className="app-nav-label app-nav-label-full">{label}</span>
        <span className="app-nav-label app-nav-label-short" aria-hidden="true">{sq ? item.shortSq : item.shortEn}</span>
      </Link>;
    })}
  </nav>;
}
