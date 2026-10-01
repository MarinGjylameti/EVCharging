import { redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import type { Notification } from "../../../../../contracts/account";

export default async function NotificationsPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  const response = await backend("/me/notifications");
  if (response.status === 401) redirect("/");
  if (!response.ok) throw new Error("Notifications unavailable.");
  const notifications = await response.json() as Notification[];
  const sq = user.language === "sq";
  return <div className="workspace-body"><span className="eyebrow">{sq ? "AKTIVITETI" : "ACTIVITY"}</span>
    <h1>{sq ? "Njoftimet" : "Notifications"}</h1>
    <p className="lead">{sq ? "50 njoftimet më të fundit të llogarisë sate." : "Your account’s 50 most recent notifications."}</p>
    {notifications.length ? <ul className="notification-list">{notifications.map(item => <li key={item.id}>
      <div className="notification-mark" aria-hidden="true">↗</div><div><h2>{item.text.title}</h2><p>{item.text.body}</p>
      <time dateTime={item.createdAt}>{new Intl.DateTimeFormat(sq ? "sq-AL" : "en-GB", {dateStyle:"medium",timeStyle:"short",timeZone:"Europe/Tirane"}).format(new Date(item.createdAt))} · Tirana</time></div>
    </li>)}</ul> : <section className="empty-state"><div className="empty-icon" aria-hidden="true">◷</div><h2>{sq ? "Ende asnjë njoftim" : "No notifications yet"}</h2><p>{sq ? "Njoftimet e llogarisë sate do të shfaqen këtu." : "Your account notifications will appear here."}</p></section>}
  </div>;
}
