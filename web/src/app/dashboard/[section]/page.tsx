import { notFound, redirect } from "next/navigation";
import { backend, currentUser } from "@/lib/backend";
import { LocationsManager } from "@/features/operations/locations-manager";
import { AdministratorsManager } from "@/features/operations/administrators-manager";
import { SessionsMonitor } from "@/features/operations/sessions-monitor";
import { HistoryView } from "@/features/operations/history-view";
import type { Station } from "../../../../../contracts/account";
import type { Administrator, Audit, Driver, Location, Price, SessionPage } from "../../../../../contracts/operations";

const titles:Record<string,[string,string]>={locations:["Vendndodhjet","Locations"],administrators:["Administratorët","Administrators"],sessions:["Monitorimi i karikimeve","Charging sessions"],audit:["Aktiviteti dhe auditimi","Activity and audit"],prices:["Historiku i çmimeve","Price history"],drivers:["Drejtuesit","Drivers"]};
export default async function OperationsPage({params}:{params:Promise<{section:string}>}){
  const [{section},user]=await Promise.all([params,currentUser()]);
  if(!user)redirect("/");if(user.role==="DRIVER")redirect("/driver");if(!titles[section])notFound();
  if((section==="administrators"||section==="drivers")&&user.role!=="SUPERADMIN")redirect("/dashboard");
  const paths=["/admin/"+section,"/admin/stations",...(section==="administrators"||section==="sessions"?["/admin/locations"]:[])];
  const responses=await Promise.all(paths.map(path=>backend(path)));
  if(responses.some(x=>x.status===401))redirect("/");if(responses.some(x=>!x.ok))throw new Error("Operations unavailable.");
  const [initial,stations,locations]=await Promise.all(responses.map(x=>x.json()));
  return <div className="workspace-body"><span className="eyebrow">{user.role}</span><h1>{titles[section][user.language==="sq"?0:1]}</h1>
    {section==="locations"?<LocationsManager initial={initial as Location[]} language={user.language} canEdit={user.role==="SUPERADMIN"}/>:section==="administrators"?<AdministratorsManager initial={initial as Administrator[]} stations={stations as Station[]} locations={locations as Location[]} language={user.language}/>:section==="sessions"?<SessionsMonitor initial={initial as SessionPage} stations={stations as Station[]} locations={locations as Location[]} language={user.language}/>:<HistoryView initial={initial as (Audit|Price|Driver)[]} kind={section as "audit"|"prices"|"drivers"} stations={stations as Station[]} language={user.language}/>}
  </div>;
}
