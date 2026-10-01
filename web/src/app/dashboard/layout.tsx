import Link from "next/link";
import { redirect } from "next/navigation";
import { Brand } from "@/components/brand";
import { SiteFooter } from "@/components/site-footer";
import { currentUser } from "@/lib/backend";
import { LogoutButton } from "@/features/auth/logout-button";
import { UserMenu } from "@/features/account/user-menu";

export default async function DashboardLayout({ children }: { children: React.ReactNode }) {
  const user = await currentUser();
  if (!user) redirect("/");
  const sq = user.language === "sq";
  if (user.role === "DRIVER") redirect("/driver");
  if (user.role !== "ADMIN" && user.role !== "SUPERADMIN")
    return <main className="simple-page"><Brand/><h1>{sq ? "Akses i kufizuar" : "Restricted access"}</h1><p>{sq ? "Kjo hapësirë kërkon një llogari administrative." : "This workspace requires an administrator account."}</p><LogoutButton language={user.language}/></main>;
  return <main className="workspace" lang={user.language}>
    <aside className="sidebar"><Brand/><div className="sidebar-caption">{sq ? "HAPËSIRA E PUNËS" : "WORKSPACE"}</div>
      <nav aria-label={sq ? "Menuja kryesore" : "Main navigation"} className="workspace-nav">
        <Link href="/dashboard"><span aria-hidden="true">▦</span>{sq ? "Përmbledhje" : "Overview"}</Link>
        <Link href="/dashboard/stations"><span aria-hidden="true">⌖</span>{sq ? "Pikat" : "Stations"}</Link>
        <Link href="/dashboard/notifications"><span aria-hidden="true">◷</span>{sq ? "Njoftimet" : "Notifications"}</Link>
        <Link href="/dashboard/account"><span aria-hidden="true">◇</span>{sq ? "Llogaria" : "Account"}</Link>
      </nav>
      <div className="sidebar-bottom"><span className="avatar">{user.firstName[0]}{user.lastName[0]}</span><div>{user.firstName} {user.lastName}<small>{user.role}</small></div></div>
    </aside>
    <section className="workspace-main"><header><span>{sq ? "Rrjeti i karikimit" : "Charging network"}</span><UserMenu initial={user}/></header><div className="app-page-content">{children}</div><SiteFooter area="dashboard" language={user.language}/></section>
  </main>;
}
