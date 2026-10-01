import Link from "next/link";

type FooterArea = "driver" | "dashboard";

export function SiteFooter({area, language}: {area: FooterArea; language: "sq" | "en"}) {
  const sq = language === "sq";
  const links = area === "driver"
    ? [
        {href: "/driver", label: sq ? "Harta e pikave" : "Charging map"},
        {href: "/driver/history", label: sq ? "Historiku im" : "My history"},
        {href: "/driver/settings", label: sq ? "Cilësimet" : "Settings"},
      ]
    : [
        {href: "/dashboard", label: sq ? "Përmbledhje" : "Overview"},
        {href: "/dashboard/stations", label: sq ? "Pikat" : "Stations"},
        {href: "/dashboard/account", label: sq ? "Llogaria" : "Account"},
      ];

  return <footer className="app-footer">
    <div className="app-footer-inner">
      <div className="app-footer-top">
        <div className="app-footer-brand">
          <span className="app-footer-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none"><path d="m13.5 2-8 12h6l-1 8 8-12h-6l1-8Z" fill="currentColor"/></svg>
          </span>
          <div><strong>EVCharging</strong><span>{sq ? "Pikat e karikimit, pranë teje." : "Charging stations, close to you."}</span></div>
        </div>
        <nav aria-label={sq ? "Lidhjet e footer-it" : "Footer navigation"}>
          {links.map(link => <Link key={link.href} href={link.href}>{link.label}</Link>)}
        </nav>
      </div>
      <div className="app-footer-bottom">
        <span>© {new Date().getFullYear()} EVCharging</span>
        <span>{sq ? "Platformë për karikimin e automjeteve elektrike" : "Electric vehicle charging platform"}</span>
      </div>
    </div>
  </footer>;
}
