import { notFound, redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import type { StationCharging } from "../../../../../../contracts/account";
import { StationChargingView } from "@/features/driver/station-charging-view";

export default async function StationPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  const user = await currentUser();
  if (!user) redirect("/");
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();
  const response = await backend(`/driver/stations/${id}/charging`);
  if (response.status === 401 || response.status === 403) redirect("/");
  if (response.status === 404) notFound();
  if (!response.ok) throw new Error("Station data unavailable.");
  return <StationChargingView key={id} initial={await response.json() as StationCharging} language={user.language} />;
}
