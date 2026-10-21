import { redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import type { DriverHistoryPage as HistoryPage } from "../../../../../contracts/account";
import { HistoryView } from "@/features/driver/history-view";

export default async function DriverHistoryPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  const response = await backend("/driver/history/search");
  if (response.status === 401) redirect("/");
  if (!response.ok) throw new Error("History unavailable.");
  const initial = await response.json() as HistoryPage;
  return <HistoryView key={user.id} initial={initial} language={user.language}/>;
}
