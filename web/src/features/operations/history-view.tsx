"use client";
import { useState } from "react";
import type { Language, Station } from "../../../../contracts/account";
import type { Audit, Price, Driver } from "../../../../contracts/operations";
import { operations } from "./api";
import { useOperationsLive } from "./use-operations-live";

const actions:Record<string,[string,string]>={
  StationCreated:["Pika u krijua","Station created"],StationUpdated:["Pika u ndryshua","Station updated"],StationArchived:["Pika u arkivua","Station archived"],
  LocationCreated:["Vendndodhja u krijua","Location created"],LocationUpdated:["Vendndodhja u ndryshua","Location updated"],LocationArchived:["Vendndodhja u arkivua","Location archived"],
  AdministratorCreated:["Administratori u krijua","Administrator created"],AdministratorUpdated:["Administratori/lejet u ndryshuan","Administrator/permissions updated"],AdministratorArchived:["Administratori u arkivua","Administrator archived"],
  ChargingStarted:["Karikimi filloi","Charging started"],ChargingCompleted:["Karikimi përfundoi","Charging completed"],ChargerConnected:["Karikuesi u lidh","Charger connected"],ChargerDisconnected:["Karikuesi u shkëput","Charger disconnected"],ChargerStatusChanged:["Gjendja e karikuesit ndryshoi","Charger status changed"],
  DeviceProvisioned:["Pajisja u regjistrua","Device registered"],DeviceCredentialsRotated:["Kredencialet u rinovuan","Credentials rotated"],DeviceAccessChanged:["Leja e pajisjes u ndryshua","Device access changed"],DeviceDriverAssigned:["Driver-i i pajisjes u caktua","Device Driver assigned"]
};
export function HistoryView({initial,kind,stations,language}:{initial:(Audit|Price|Driver)[];kind:"audit"|"prices"|"drivers";stations:Station[];language:Language}){
  const sq=language==="sq";const [items,setItems]=useState(initial);
  const live=useOperationsLive(async signal=>{const next=await operations<(Audit|Price|Driver)[]>(kind,signal);if(!signal.aborted)setItems(next);});
  const fmt=(date:string)=>new Date(date).toLocaleString(sq?"sq-AL":"en-GB");const name=(id:string)=>stations.find(x=>x.id===id)?.name??id;
  const columns=kind==="audit"?[sq?"Koha":"Time",sq?"Veprimi":"Action",sq?"Burimi":"Resource",sq?"Aktori / Roli":"Actor / Role",sq?"Para / Pas / Arsyeja":"Before / After / Reason"]:kind==="prices"?[sq?"Koha":"Time",sq?"Pika":"Station",sq?"Para":"Before",sq?"Pas":"After",sq?"Arsyeja / Aktori":"Reason / Actor"]:[sq?"Emri":"Name","Email",sq?"Celulari":"Phone",sq?"Gjendja":"Status",sq?"Regjistruar":"Registered"];
  return <><p role="status">{live?(sq?"Përditësime aktive":"Live updates"):(sq?"Duke lidhur përditësimet…":"Connecting live updates…")}{kind!=="drivers"?(sq?" · 200 regjistrimet më të fundit":" · Latest 200 records"):""}</p>
    <div className="operations-table-wrap"><table className="operations-table"><thead><tr>{columns.map(x=><th key={x}>{x}</th>)}</tr></thead><tbody>
      {items.map(row=>"action" in row?<tr key={row.id}><td>{fmt(row.recordedAt)}</td><td>{actions[row.action]?.[sq?0:1]??row.action}</td><td>{row.resourceName??name(row.entityId)}</td><td>{row.actorRole}<small>{row.actorRole==="Device"?(sq?"Karikuesi":"Charger"):row.actorName??row.actorId}</small></td><td><details><summary>{sq?"Shiko ndryshimet":"View changes"}</summary><pre>{row.changesJson??"—"}</pre></details></td></tr>:"pricePerKWh" in row?<tr key={row.id}><td>{fmt(row.recordedAt)}</td><td>{name(row.stationId)}</td><td>{row.previousPrice??"—"}</td><td>{row.pricePerKWh} {row.currency}/kWh</td><td>{row.reason}<small>{row.actorId}</small></td></tr>:<tr key={row.id}><td>{row.firstName} {row.lastName}</td><td>{row.email}</td><td>{row.phone??"—"}</td><td>{row.isActive?(sq?"Aktiv":"Active"):(sq?"Joaktiv":"Inactive")}</td><td>{fmt(row.createdAt)}</td></tr>)}
    </tbody></table>{!items.length&&<p>{sq?"Nuk ka regjistrime.":"No records."}</p>}</div>
  </>;
}
