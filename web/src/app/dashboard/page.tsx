import { redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import Link from "next/link";
import type { Station } from "../../../../contracts/account";

export default async function Dashboard() {
  const user = await currentUser();
  if (!user) redirect("/");
  const sq = user.language === "sq";
  const response = await backend("/admin/stations");
  const stations = response.ok ? await response.json() as Station[] : [];
  const active = stations.filter(x=>!x.isArchived);
  const available = active.filter(x=>x.status==="Available").length;
  return <div className="workspace-body"><span className="eyebrow">{sq ? "MIRË SE ERDHE" : "WELCOME"}</span>
    <h1>{sq ? "Hapësira jote e menaxhimit." : "Your operations workspace."}</h1>
    <p className="lead">{sq ? "Llogaria jote administrative është aktive." : "Your administrator account is active."}</p>
    {active.length ? <><div className="station-summary"><article><span>{sq?"PIKA AKTIVE":"ACTIVE STATIONS"}</span><strong>{active.length}</strong></article>
      <article><span>{sq?"TË LIRA":"AVAILABLE"}</span><strong>{available}</strong></article>
      <article><span>{sq?"QYTETE":"CITIES"}</span><strong>{new Set(active.map(x=>x.city)).size}</strong></article></div>
      {active.some(x=>x.isDemo)&&<p className="station-disclaimer">{sq?"Këto shifra përfshijnë pika demo; nuk janë telemetri nga karikuesit.":"These figures include demo stations; they are not charger telemetry."}</p>}
      <Link className="button-outline inline-link" href="/dashboard/stations">{sq?"Shiko pikat →":"View stations →"}</Link></> :
      <section className="empty-state"><div className="empty-icon" aria-hidden="true">⌁</div><h2>{sq ? "Rrjeti ende nuk është konfiguruar" : "The network is not configured yet"}</h2>
        <p>{sq ? "Shto pikën e parë të karikimit për ta shfaqur në hartën e drejtuesit." : "Add the first station to show it on the driver map."}</p>
        <Link className="button-outline inline-link" href="/dashboard/stations">{sq?"Shiko pikat →":"View stations →"}</Link></section>}
    <div className="account-summary"><span>{sq ? "Llogaria" : "Account"}<strong>{user.email}</strong></span><span>{sq ? "Roli" : "Role"}<strong>{user.role}</strong></span>
      <span>{sq ? "Gjuha" : "Language"}<strong>{sq ? "Shqip" : "English"}</strong></span></div>
    <Link className="button-outline inline-link" href="/dashboard/account">{sq ? "Menaxho llogarinë →" : "Manage account →"}</Link>
  </div>;
}
