import { notFound, redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import type { StationCharging } from "../../../../../../contracts/account";
import { StationChargingView } from "@/features/driver/station-charging-view";

export default async function StationPage({ params, searchParams }: { params: Promise<{ id: string }>; searchParams: Promise<{ connector?: string }> }) {
  const { id } = await params;
  const user = await currentUser();
  if (!user) redirect("/");
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();
  const { connector } = await searchParams;
  if (connector !== undefined && !/^[1-9][0-9]{0,2}$/.test(connector)) notFound();
  const response = await backend(`/driver/stations/${id}/charging${connector ? `?connector=${connector}` : ""}`);
  if (response.status === 401 || response.status === 403) redirect("/");
  if (response.status === 404 || response.status === 400) notFound();
  if (!response.ok) throw new Error("Station data unavailable.");
  const initial = await response.json() as StationCharging;
  return <StationChargingView key={`${id}:${initial.connector?.number ?? 1}`} initial={initial} language={user.language} />;
}
