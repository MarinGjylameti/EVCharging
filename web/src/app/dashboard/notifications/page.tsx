import { redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import type { Notification } from "../../../../../contracts/account";
import { OperationsNotifications } from "@/features/operations/operations-notifications";

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
    <OperationsNotifications initial={notifications} language={user.language}/>
  </div>;
}
