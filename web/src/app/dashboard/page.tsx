import { redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import { OperationsOverview } from "@/features/operations/operations-overview";
import type { Summary } from "../../../../contracts/operations";
export default async function Dashboard(){
  const user=await currentUser();if(!user)redirect("/");if(user.role==="DRIVER")redirect("/driver");
  const response=await backend("/admin/summary");if(response.status===401)redirect("/");if(!response.ok)throw new Error("Operations unavailable.");
  return <OperationsOverview initial={await response.json() as Summary} language={user.language} superadmin={user.role==="SUPERADMIN"}/>;
}
