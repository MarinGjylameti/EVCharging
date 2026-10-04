"use client";
import { useState } from "react";
import Link from "next/link";
import type { Language } from "../../../../contracts/account";
import type { Summary } from "../../../../contracts/operations";
import { operations } from "./api";
import { useOperationsLive } from "./use-operations-live";

const labels:Record<string,[string,string]>={Available:["Të lira","Available"],Occupied:["Të zëna","Occupied"],Charging:["Në karikim","Charging"],OutOfService:["Jashtë funksionit","Out of service"],Maintenance:["Në mirëmbajtje","Maintenance"],Unknown:["Pa informacion aktual","Unknown"]};
export function OperationsOverview({initial,language,superadmin}:{initial:Summary;language:Language;superadmin:boolean}){
  const sq=language==="sq";const [data,setData]=useState(initial);
  const live=useOperationsLive(async signal=>{const next=await operations<Summary>("summary",signal);if(!signal.aborted)setData(next);});
  const stats:[string,number|string][]=[[sq?"Pika aktive":"Active stations",data.stations],[sq?"Vendndodhje aktive":"Active locations",data.locations],...Object.entries(data.statusCounts).map(([key,value]):[string,number]=>[labels[key]?.[sq?0:1]??key,value]),[sq?"Karikime aktive":"Active sessions",data.activeSessions],[sq?"Karikime të përfunduara":"Completed sessions",data.completedSessions],[sq?"Energji e regjistruar":"Recorded energy",`${data.energyKWh.toFixed(3)} kWh`],[sq?"Kosto e llogaritur":"Calculated cost",`${data.calculatedCost.toFixed(2)} ${data.currency}`]];
  if(superadmin)stats.push([sq?"Administratorë aktivë":"Active administrators",data.administrators],[sq?"Drejtues aktivë":"Active drivers",data.drivers]);
  return <div className="workspace-body"><span className="eyebrow">{sq?"PANELI OPERATIV":"OPERATIONS DASHBOARD"}</span><h1>{sq?"Rrjeti i karikimit":"Charging network"}</h1><p role="status">{live?(sq?"Përditësime aktive":"Live updates"):(sq?"Duke lidhur përditësimet…":"Connecting live updates…")}</p>
    <div className="operations-stats">{stats.map(([title,value])=><article key={title}><span>{title}</span><strong>{value}</strong></article>)}</div>
    <p>{sq?"Sesione fizike të regjistruara, përfshirë testet demo. Kostot janë llogaritje nga tarifat e sesioneve.":"Recorded physical sessions, including demo tests. Costs are calculated from session tariffs."}</p>
    <div className="operations-actions"><Link href="/dashboard/stations" className="button-primary inline-link">{sq?"Menaxho pikat":"Manage stations"}</Link><Link href="/dashboard/sessions" className="button-outline inline-link">{sq?"Monitoro karikimet":"Monitor sessions"}</Link><Link href="/dashboard/audit" className="button-outline inline-link">{sq?"Aktiviteti dhe auditimi":"Activity and audit"}</Link></div>
  </div>;
}
