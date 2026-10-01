import { redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import type { ChargingHistory } from "../../../../../contracts/account";
import { HistoryView } from "@/features/driver/history-view";

export default async function DriverHistoryPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  const response = await backend("/driver/history");
  if (response.status === 401) redirect("/");
  if (!response.ok) throw new Error("History unavailable.");
  const history = await response.json() as ChargingHistory[];
  return <HistoryView history={history} language={user.language}/>;
}
