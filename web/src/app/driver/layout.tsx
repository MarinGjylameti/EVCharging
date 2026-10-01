import Link from "next/link";
import { redirect } from "next/navigation";
import { Brand } from "@/components/brand";
import { SiteFooter } from "@/components/site-footer";
import { currentUser } from "@/lib/backend";
import { UserMenu } from "@/features/account/user-menu";

export default async function DriverLayout({children}: {children: React.ReactNode}) {
  const user = await currentUser();
  if (!user) redirect("/");
  if (user.role !== "DRIVER") redirect("/dashboard");
  const sq = user.language === "sq";
  return <main className="driver-shell" lang={user.language}>
    <aside className="driver-sidebar">
      <Brand/>
      <div className="driver-side-caption">{sq ? "HAPËSIRA E DREJTUESIT" : "DRIVER WORKSPACE"}</div>
      <nav aria-label={sq ? "Menuja kryesore" : "Main navigation"}>
        <Link href="/driver"><span aria-hidden="true">⌖</span>{sq ? "Harta e pikave" : "Charging map"}</Link>
        <Link href="/driver/history"><span aria-hidden="true">◷</span>{sq ? "Historiku im" : "My history"}</Link>
        <Link href="/driver/settings"><span aria-hidden="true">⚙</span>{sq ? "Cilësimet" : "Settings"}</Link>
      </nav>
      <div className="driver-side-footer">
        <span className="avatar">{user.firstName[0]}{user.lastName[0]}</span>
        <span>{user.firstName} {user.lastName}<small>{sq ? "Llogari drejtuesi" : "Driver account"}</small></span>
      </div>
    </aside>
    <section className="driver-main">
      <header className="driver-header">
        <div><span className="driver-header-kicker">EVCHARGING / {sq ? "DREJTUESI" : "DRIVER"}</span>
          <strong>{sq ? "Gjej pikën tënde" : "Find your charge"}</strong></div>
        <UserMenu initial={user}/>
      </header>
      <div className="app-page-content">{children}</div>
      <SiteFooter area="driver" language={user.language}/>
    </section>
  </main>;
}
