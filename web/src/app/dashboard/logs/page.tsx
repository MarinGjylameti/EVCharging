import { redirect } from "next/navigation";
import { currentUser } from "@/lib/backend";
import { ApplicationLogs } from "@/features/operations/application-logs";

export default async function LogsPage() {
  const user=await currentUser();
  if(!user)redirect("/");
  if(user.role!=="SUPERADMIN")redirect(user.role==="DRIVER"?"/driver":"/dashboard");
  return <div className="workspace-body"><span className="eyebrow">SUPERADMIN</span><h1>{user.language==="sq"?"Loget e lidhjes":"Connection logs"}</h1><ApplicationLogs language={user.language}/></div>;
}
