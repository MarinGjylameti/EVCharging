import { redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import type { ChargingHistory, Station } from "../../../../contracts/account";
import { DriverMap } from "@/features/driver/driver-map";

export default async function DriverPage() {
  const user = await currentUser();
  if (!user) redirect("/");
  const [stationResponse, historyResponse] = await Promise.all([
    backend("/driver/stations"), backend("/driver/history")
  ]);
  if (stationResponse.status === 401 || historyResponse.status === 401) redirect("/");
  if (!stationResponse.ok || !historyResponse.ok) throw new Error("Driver data unavailable.");
  const stations = await stationResponse.json() as Station[];
  const history = await historyResponse.json() as ChargingHistory[];
  return <DriverMap stations={stations} history={history} language={user.language} firstName={user.firstName}/>;
}
