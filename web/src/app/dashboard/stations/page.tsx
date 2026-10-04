import { redirect } from "next/navigation";
import { apiOrigin, backend, currentUser } from "@/lib/backend";
import type { Location } from "../../../../../contracts/operations";
import { StationManager } from "@/features/stations/station-manager";
import type { Station } from "../../../../../contracts/account";

export default async function StationsPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  if (user.role === "DRIVER") redirect("/driver");
  const [response,places] = await Promise.all([backend("/admin/stations"),backend("/admin/locations")]);
  if (response.status === 401) redirect("/");
  if (!response.ok) throw new Error("Stations unavailable.");
  const stations = await response.json() as Station[];
  if(!places.ok) throw new Error("Locations unavailable.");
  const locations=await places.json() as Location[];
  const webSocketBaseUrl=apiOrigin().replace(/^https:/,"wss:").replace(/^http:/,"ws:")+"/ws/cs";
  return <StationManager initial={stations} locations={locations} language={user.language} canEdit superadmin={user.role==="SUPERADMIN"} webSocketBaseUrl={webSocketBaseUrl}/>;
}
