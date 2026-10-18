"use client";
import { useEffect, useRef, useState } from "react";
import type { Language, Station } from "../../../../contracts/account";
import type { Location, SessionPage } from "../../../../contracts/operations";
import { failure, operations } from "./api";
import { useOperationsLive } from "./use-operations-live";

const blank={stationId:"",locationId:"",status:"",from:"",until:"",driver:"",page:1};
export function sessionQuery(filter:typeof blank){
  const from = filter.from ? Date.parse(filter.from) : null, until = filter.until ? Date.parse(filter.until) : null;
  if (from !== null && !Number.isFinite(from) || until !== null && !Number.isFinite(until) || from !== null && until !== null && from >= until)
    throw new Error("invalid_session_dates");
  const params=new URLSearchParams();
  Object.entries(filter).forEach(([key,value])=>{if(value!=="")params.set(key,key==="from"||key==="until"?new Date(String(value)).toISOString():String(value));});
  return "&"+params;
}
const states:Record<string,[string,string]>={Connecting:["Duke nisur","Connecting"],Charging:["Në karikim","Charging"],PausedByVehicle:["Në pauzë nga makina","Paused by vehicle"],PausedByCharger:["Në pauzë nga karikuesi","Paused by charger"],Finishing:["Duke përfunduar","Finishing"],Completed:["Përfunduar","Completed"],Faulted:["Defekt","Faulted"],ConnectionLost:["Lidhja u humb","Connection lost"]};
export function SessionsMonitor({initial,stations,locations,language}:{initial:SessionPage;stations:Station[];locations:Location[];language:Language}){
  const sq=language==="sq";const [data,setData]=useState(initial);const [draft,setDraft]=useState(blank);const [filter,setFilter]=useState(blank);const [error,setError]=useState("");const [now,setNow]=useState(Date.now());
  const query = sessionQuery(filter), currentQuery = useRef(query), readSequence = useRef(0);
  currentQuery.current = query;
  async function refresh(signal:AbortSignal){const sequence=++readSequence.current;const next=await operations<SessionPage>("sessions",signal,query);if(!signal.aborted && sequence===readSequence.current && query===currentQuery.current){setData(next);setError("");}}
  const live=useOperationsLive(refresh,true);
  useEffect(()=>{const controller=new AbortController();void refresh(controller.signal).catch(e=>{if(!controller.signal.aborted)setError(failure(e,sq));});return()=>controller.abort();},[filter,sq]);
  useEffect(()=>{const timer=setInterval(()=>setNow(Date.now()),1000);return()=>clearInterval(timer);},[]);
  const fmt=(value:string)=>new Date(value).toLocaleString(sq?"sq-AL":"en-GB");
  return <div className="operations-view"><p className="operations-live-status" data-state={live?"active":"loading"} role="status">{live?(sq?"Përditësime aktive":"Live updates"):(sq?"Duke lidhur përditësimet…":"Connecting live updates…")}</p><details className="session-filter-panel"><summary>{sq ? "Filtro karikimet" : "Filter sessions"}{Object.entries(filter).some(([key,value])=>key!=="page" && value!=="") ? (sq ? " · Filtra aktivë" : " · Filters applied") : ""}</summary><form className="operations-filters" aria-label={sq?"Filtrat e karikimeve":"Charging filters"} onSubmit={e=>{e.preventDefault();try {sessionQuery(draft);setError("");setFilter({...draft,page:1});}catch {setError(sq ? "Data e fundit duhet të jetë pas datës së fillimit." : "The end date must be after the start date.");}}}>
    <div className="operations-filter-heading"><h2>{sq?"Filtrat e karikimeve":"Charging filters"}</h2><p>{sq?"Kërko sipas pikës, gjendjes ose drejtuesit.":"Search by station, status or driver."}</p></div>
    <div className="operations-filter-grid">
    <label>{sq?"Pika":"Station"}<select value={draft.stationId} onChange={e=>setDraft({...draft,stationId:e.target.value})}><option value="">{sq?"Të gjitha":"All"}</option>{stations.map(s=><option value={s.id} key={s.id}>{s.code} · {s.name}</option>)}</select></label>
    <label>{sq?"Vendndodhja":"Location"}<select value={draft.locationId} onChange={e=>setDraft({...draft,locationId:e.target.value})}><option value="">{sq?"Të gjitha":"All"}</option>{locations.map(l=><option value={l.id} key={l.id}>{l.name}</option>)}</select></label>
    <label>{sq?"Gjendja":"Status"}<select value={draft.status} onChange={e=>setDraft({...draft,status:e.target.value})}><option value="">{sq?"Të gjitha":"All"}</option><option value="Active">{sq?"Aktive":"Active"}</option><option value="Completed">{sq?"Të përfunduara":"Completed"}</option><option value="Charging">{sq?"Në karikim":"Charging"}</option><option value="Faulted">{sq?"Me defekt":"Faulted"}</option><option value="PausedByVehicle">{sq?"Në pauzë nga makina":"Paused by vehicle"}</option><option value="PausedByCharger">{sq?"Në pauzë nga karikuesi":"Paused by charger"}</option><option value="Connecting">{sq?"Duke nisur":"Connecting"}</option><option value="Finishing">{sq?"Duke përfunduar":"Finishing"}</option><option value="ConnectionLost">{sq?"Lidhja u humb":"Connection lost"}</option></select></label>
    <label>{sq?"Nga (përfshihet)":"From (inclusive)"}<input type="datetime-local" value={draft.from} onChange={e=>setDraft({...draft,from:e.target.value})}/></label><label>{sq?"Deri (nuk përfshihet)":"Until (exclusive)"}<input type="datetime-local" value={draft.until} onChange={e=>setDraft({...draft,until:e.target.value})}/></label>
    <label>{sq?"Drejtuesi (emër/email)":"Driver (name/email)"}<input maxLength={254} value={draft.driver} placeholder={sq?"Kërko drejtuesin":"Search for a driver"} onChange={e=>setDraft({...draft,driver:e.target.value})}/></label>
    </div>
    <div className="operations-filter-actions"><button type="submit" className="button-primary">{sq?"Filtro":"Filter"}</button><button className="button-outline" type="button" onClick={()=>{setDraft(blank);setFilter(blank);}}>{sq?"Pastro":"Reset"}</button><span className="operations-filter-hint">{sq?"Datat dhe orët shfaqen sipas orës lokale.":"Dates and times use your local time zone."}</span></div>
  </form></details>{error&&<p className="form-error" role="alert">{error}</p>}
  <div className="operations-table-wrap sessions-responsive" tabIndex={0} role="region" aria-label={sq?"Tabela e karikimeve":"Charging sessions table"}><table className="operations-table operations-sessions-table" role="table"><thead role="rowgroup"><tr role="row">{[sq?"Pika / Sesioni":"Station / Session",sq?"Drejtuesi":"Driver",sq?"Gjendja":"Status",sq?"Fillimi / Fundi":"Start / End",sq?"Kohëzgjatja":"Duration","kWh","kW",sq?"Bateria":"Battery",sq?"Tarifa / Kosto":"Tariff / Cost"].map(x=><th scope="col" role="columnheader" key={x}>{x}</th>)}</tr></thead><tbody role="rowgroup">
    {data.items.map(s=><tr key={s.id} role="row">
      <td role="cell" data-label={sq?"Pika / Sesioni":"Station / Session"}><strong>{s.stationCode}</strong><br/>{s.stationName}<small>{sq?"Lidhësi":"Connector"} {s.connectorNumber}{s.isDemo?" · Demo":""}</small><details><summary>ID</summary>{s.id}</details></td>
      <td role="cell" data-label={sq?"Drejtuesi":"Driver"}>{s.driverName??(sq?"Pa llogari të verifikuar":"No verified account")}<small>{s.driverEmail}</small></td>
      <td role="cell" data-label={sq?"Gjendja":"Status"}><span className="session-status" data-status={s.status}>{states[s.status]?.[sq?0:1]??s.status}</span></td>
      <td role="cell" data-label={sq?"Fillimi / Fundi":"Start / End"}>{fmt(s.startedAt)}<small>{s.endedAt?fmt(s.endedAt):"—"}</small></td>
      <td role="cell" data-label={sq?"Kohëzgjatja":"Duration"}>{Math.max(0,Math.floor(((s.endedAt?Date.parse(s.endedAt):now)-Date.parse(s.startedAt))/60000))} min</td>
      <td role="cell" data-label={sq?"Energjia (kWh)":"Energy (kWh)"}>{s.energyKWh?.toFixed(3)??"—"}</td>
      <td role="cell" data-label={sq?"Fuqia (kW)":"Power (kW)"}>{s.powerKW??"—"}</td>
      <td role="cell" data-label={sq?"Bateria":"Battery"}>{s.stateOfCharge===null?"—":s.stateOfCharge+"%"}</td>
      <td role="cell" data-label={sq?"Tarifa / Kosto":"Tariff / Cost"}>{s.unitPriceSnapshot} {s.currency}/kWh<small>{s.cost?.toFixed(2)??"—"} {s.currency}</small></td>
    </tr>)}
  </tbody></table>{!data.items.length&&<p className="operations-empty">{sq?"Nuk u gjetën sesione për këta filtra.":"No sessions match these filters."}</p>}</div>
  <div className="operations-actions operations-pagination"><button type="button" className="button-outline" disabled={filter.page<=1} onClick={()=>setFilter({...filter,page:filter.page-1})}>{sq?"Më parë":"Previous"}</button><span>{data.total} {sq?"sesione":"sessions"} · {sq?"Faqja":"Page"} {data.page}</span><button type="button" className="button-outline" disabled={filter.page*data.pageSize>=data.total} onClick={()=>setFilter({...filter,page:filter.page+1})}>{sq?"Më tej":"Next"}</button></div>
  </div>;
}
