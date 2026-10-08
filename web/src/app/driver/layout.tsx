import { redirect } from "next/navigation";
import { Brand } from "@/components/brand";
import { AppNav } from "@/components/app-nav";
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
      <AppNav area="driver" language={user.language}/>
      <div className="driver-side-footer">
        <span className="avatar">{user.firstName[0]}{user.lastName[0]}</span>
        <span>{user.firstName} {user.lastName}<small>{sq ? "Llogari drejtuesi" : "Driver account"}</small></span>
      </div>
    </aside>
    <section className="driver-main">
      <header className="driver-header">
        <div className="app-mobile-brand"><Brand/></div>
        <div className="driver-header-title"><span className="driver-header-kicker">EVCHARGING / {sq ? "DREJTUESI" : "DRIVER"}</span>
          <strong>{sq ? "Gjej pikën tënde" : "Find your charge"}</strong></div>
        <UserMenu initial={user}/>
      </header>
      <div className="app-page-content">{children}</div>
      <SiteFooter area="driver" language={user.language}/>
    </section>
  </main>;
}
