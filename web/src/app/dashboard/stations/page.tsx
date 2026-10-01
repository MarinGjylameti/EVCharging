import { redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import { StationManager } from "@/features/stations/station-manager";
import type { Station } from "../../../../../contracts/account";

export default async function StationsPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  if (user.role === "DRIVER") redirect("/driver");
  const response = await backend("/admin/stations");
  if (response.status === 401) redirect("/");
  if (!response.ok) throw new Error("Stations unavailable.");
  const stations = await response.json() as Station[];
  return <StationManager initial={stations} language={user.language} canEdit={user.role === "SUPERADMIN"}/>;
}
