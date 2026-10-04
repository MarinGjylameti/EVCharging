"use client";
import { useEffect, useState } from "react";
import type { Language, Station } from "../../../../contracts/account";
import type { Location, SessionPage } from "../../../../contracts/operations";
import { failure, operations } from "./api";
import { useOperationsLive } from "./use-operations-live";

const blank={stationId:"",locationId:"",status:"",from:"",until:"",driver:"",page:1};
export function sessionQuery(filter:typeof blank){
  const params=new URLSearchParams();
  Object.entries(filter).forEach(([key,value])=>{if(value!=="")params.set(key,key==="from"||key==="until"?new Date(String(value)).toISOString():String(value));});
  return "&"+params;
}
const states:Record<string,[string,string]>={Connecting:["Duke nisur","Connecting"],Charging:["Në karikim","Charging"],PausedByVehicle:["Në pauzë nga makina","Paused by vehicle"],PausedByCharger:["Në pauzë nga karikuesi","Paused by charger"],Finishing:["Duke përfunduar","Finishing"],Completed:["Përfunduar","Completed"],Faulted:["Defekt","Faulted"],ConnectionLost:["Lidhja u humb","Connection lost"]};
export function SessionsMonitor({initial,stations,locations,language}:{initial:SessionPage;stations:Station[];locations:Location[];language:Language}){
  const sq=language==="sq";const [data,setData]=useState(initial);const [draft,setDraft]=useState(blank);const [filter,setFilter]=useState(blank);const [error,setError]=useState("");const [now,setNow]=useState(Date.now());
  async function refresh(signal:AbortSignal){const next=await operations<SessionPage>("sessions",signal,sessionQuery(filter));if(!signal.aborted){setData(next);setError("");}}
  const live=useOperationsLive(refresh);
  useEffect(()=>{const controller=new AbortController();void refresh(controller.signal).catch(e=>{if(!controller.signal.aborted)setError(failure(e,sq));});return()=>controller.abort();},[filter,sq]);
  useEffect(()=>{const timer=setInterval(()=>setNow(Date.now()),1000);return()=>clearInterval(timer);},[]);
  const fmt=(value:string)=>new Date(value).toLocaleString(sq?"sq-AL":"en-GB");
  return <><p role="status">{live?(sq?"Përditësime aktive":"Live updates"):(sq?"Duke lidhur përditësimet…":"Connecting live updates…")}</p><form className="operations-filters" onSubmit={e=>{e.preventDefault();setFilter({...draft,page:1});}}>
    <label>{sq?"Pika":"Station"}<select value={draft.stationId} onChange={e=>setDraft({...draft,stationId:e.target.value})}><option value="">{sq?"Të gjitha":"All"}</option>{stations.map(s=><option value={s.id} key={s.id}>{s.code} · {s.name}</option>)}</select></label>
    <label>{sq?"Vendndodhja":"Location"}<select value={draft.locationId} onChange={e=>setDraft({...draft,locationId:e.target.value})}><option value="">{sq?"Të gjitha":"All"}</option>{locations.map(l=><option value={l.id} key={l.id}>{l.name}</option>)}</select></label>
    <label>{sq?"Gjendja":"Status"}<select value={draft.status} onChange={e=>setDraft({...draft,status:e.target.value})}><option value="">{sq?"Të gjitha":"All"}</option><option value="Active">{sq?"Aktive":"Active"}</option><option value="Completed">{sq?"Të përfunduara":"Completed"}</option><option value="Charging">{sq?"Në karikim":"Charging"}</option><option value="Faulted">{sq?"Me defekt":"Faulted"}</option><option value="PausedByVehicle">{sq?"Në pauzë nga makina":"Paused by vehicle"}</option><option value="PausedByCharger">{sq?"Në pauzë nga karikuesi":"Paused by charger"}</option><option value="Connecting">{sq?"Duke nisur":"Connecting"}</option><option value="Finishing">{sq?"Duke përfunduar":"Finishing"}</option><option value="ConnectionLost">{sq?"Lidhja u humb":"Connection lost"}</option></select></label>
    <label>{sq?"Nga (përfshihet)":"From (inclusive)"}<input type="datetime-local" value={draft.from} onChange={e=>setDraft({...draft,from:e.target.value})}/></label><label>{sq?"Deri (nuk përfshihet)":"Until (exclusive)"}<input type="datetime-local" value={draft.until} onChange={e=>setDraft({...draft,until:e.target.value})}/></label>
    <label>{sq?"Drejtuesi (emër/email)":"Driver (name/email)"}<input maxLength={254} value={draft.driver} onChange={e=>setDraft({...draft,driver:e.target.value})}/></label><button className="button-primary">{sq?"Filtro":"Filter"}</button><button className="button-outline" type="button" onClick={()=>{setDraft(blank);setFilter(blank);}}>{sq?"Pastro":"Reset"}</button>
  </form>{error&&<p className="form-error" role="alert">{error}</p>}
  <div className="operations-table-wrap"><table className="operations-table"><thead><tr>{[sq?"Pika / Sesioni":"Station / Session",sq?"Drejtuesi":"Driver",sq?"Gjendja":"Status",sq?"Fillimi / Fundi":"Start / End",sq?"Kohëzgjatja":"Duration","kWh","kW","SoC",sq?"Tarifa / Kosto":"Tariff / Cost"].map(x=><th key={x}>{x}</th>)}</tr></thead><tbody>
    {data.items.map(s=><tr key={s.id}><td><strong>{s.stationCode}</strong><br/>{s.stationName}<small>{sq?"Lidhësi":"Connector"} {s.connectorNumber}{s.isDemo?" · Demo":""}</small><details><summary>ID</summary>{s.id}</details></td><td>{s.driverName??(sq?"Pa llogari të verifikuar":"No verified account")}<small>{s.driverEmail}</small></td><td>{states[s.status]?.[sq?0:1]??s.status}</td><td>{fmt(s.startedAt)}<small>{s.endedAt?fmt(s.endedAt):"—"}</small></td><td>{Math.max(0,Math.floor(((s.endedAt?Date.parse(s.endedAt):now)-Date.parse(s.startedAt))/60000))} min</td><td>{s.energyKWh?.toFixed(3)??"—"}</td><td>{s.powerKW??"—"}</td><td>{s.stateOfCharge===null?"—":s.stateOfCharge+"%"}</td><td>{s.unitPriceSnapshot} {s.currency}/kWh<small>{s.cost?.toFixed(2)??"—"} {s.currency}</small></td></tr>)}
  </tbody></table>{!data.items.length&&<p>{sq?"Nuk u gjetën sesione për këta filtra.":"No sessions match these filters."}</p>}</div>
  <div className="operations-actions"><button type="button" className="button-outline" disabled={filter.page<=1} onClick={()=>setFilter({...filter,page:filter.page-1})}>{sq?"Më parë":"Previous"}</button><span>{data.total} · {sq?"Faqja":"Page"} {data.page}</span><button type="button" className="button-outline" disabled={filter.page*data.pageSize>=data.total} onClick={()=>setFilter({...filter,page:filter.page+1})}>{sq?"Më tej":"Next"}</button></div>
  </>;
}
