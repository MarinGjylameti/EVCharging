"use client";
import { useState } from "react";
import type { Language, Station } from "../../../../contracts/account";
import type { Administrator, Location } from "../../../../contracts/operations";
import { failure, mutate, operations } from "./api";
import { useOperationsLive } from "./use-operations-live";

const blank={email:"",firstName:"",lastName:"",phone:"",password:"",isActive:true,locationIds:[] as string[],stationIds:[] as string[],version:0};
export function AdministratorsManager({initial,locations:initialLocations,stations:initialStations,language}:{initial:Administrator[];locations:Location[];stations:Station[];language:Language}){
  const sq=language==="sq";const [items,setItems]=useState(initial);const [locations,setLocations]=useState(initialLocations);const [stations,setStations]=useState(initialStations);const [selected,setSelected]=useState<string|null>(null);const [draft,setDraft]=useState(blank);const [busy,setBusy]=useState(false);const [error,setError]=useState("");const [message,setMessage]=useState("");
  useOperationsLive(async signal=>{const [people,places,points]=await Promise.all([operations<Administrator[]>("administrators",signal),operations<Location[]>("locations",signal),operations<Station[]>("stations",signal)]);if(!signal.aborted){setItems(people);setLocations(places);setStations(points);}});
  function select(a:Administrator){setSelected(a.id);setDraft({email:a.email,firstName:a.firstName,lastName:a.lastName,phone:a.phone??"",password:"",isActive:a.isActive,locationIds:a.locationIds,stationIds:a.stationIds,version:a.version});setError("");setMessage("");}
  function grant(key:"locationIds"|"stationIds",id:string,checked:boolean){setDraft(previous=>({...previous,[key]:checked?[...previous[key],id]:previous[key].filter(x=>x!==id)}));}
  async function save(archive=false){
    if(busy)return;if(archive&&!window.confirm(sq?"Arkivo administratorin dhe revoko hyrjet e tij?":"Archive this administrator and revoke their sessions?"))return;setBusy(true);setError("");setMessage("");
    try{const row=await mutate<Administrator>("administrators",archive?"archive":selected?"update":"create",archive?{version:draft.version}:draft,selected??undefined);setItems(previous=>[...previous.filter(x=>x.id!==row.id),row]);select(row);setMessage(sq?"Administratori u ruajt.":"Administrator saved.");}
    catch(e){setError(failure(e,sq));}finally{setBusy(false);}
  }
  const archived=!!items.find(x=>x.id===selected)?.archivedAt;
  return <div className="operations-grid"><section className="operations-card"><h2>{sq?"Administratorët":"Administrators"}</h2><button type="button" className="button-outline" onClick={()=>{setSelected(null);setDraft({...blank});setError("");setMessage("");}}>{sq?"Shto administrator":"Add administrator"}</button>
    {items.map(a=><button type="button" key={a.id} className="station-admin-row" aria-pressed={selected===a.id} onClick={()=>select(a)}><span><strong>{a.firstName} {a.lastName}</strong><small>{a.email} · {a.archivedAt?(sq?"Arkivuar":"Archived"):a.isActive?(sq?"Aktiv":"Active"):(sq?"Joaktiv":"Inactive")}</small></span></button>)}
  </section><section className="operations-card"><h2>{sq?"Llogaria dhe lejet":"Account and permissions"}</h2>{error&&<p className="form-error" role="alert">{error}</p>}{message&&<p className="form-success" role="status">{message}</p>}
    <form onSubmit={e=>{e.preventDefault();void save();}}><fieldset className="station-form-fields" disabled={busy||archived}>
      <div className="station-form-pair"><label>{sq?"Emri":"First name"}<input required maxLength={80} value={draft.firstName} onChange={e=>setDraft({...draft,firstName:e.target.value})}/></label><label>{sq?"Mbiemri":"Last name"}<input required maxLength={80} value={draft.lastName} onChange={e=>setDraft({...draft,lastName:e.target.value})}/></label></div>
      <label>Email<input required type="email" maxLength={254} autoComplete="off" value={draft.email} onChange={e=>setDraft({...draft,email:e.target.value})}/></label><label>{sq?"Celulari (opsional, me prefiks)":"Phone (optional, country code)"}<input type="tel" value={draft.phone} onChange={e=>setDraft({...draft,phone:e.target.value})} placeholder="+355691234567"/></label>
      <label>{selected?(sq?"Fjalëkalim i ri (opsional)":"New password (optional)"):(sq?"Fjalëkalimi fillestar":"Initial password")}<input type="password" required={!selected} minLength={12} maxLength={128} autoComplete="new-password" value={draft.password} onChange={e=>setDraft({...draft,password:e.target.value})}/></label>
      <label className="operations-check"><input type="checkbox" checked={draft.isActive} onChange={e=>setDraft({...draft,isActive:e.target.checked})}/>{sq?"Llogari aktive":"Active account"}</label>
      <h3>{sq?"Vendndodhje të caktuara":"Assigned locations"}</h3><small>{sq?"Lejon të gjitha pikat në vendndodhje dhe krijimin e pikave të reja aty.":"Allows all stations in a location and creation of new stations there."}</small>
      {locations.filter(l=>!l.isArchived||draft.locationIds.includes(l.id)).map(l=><label className="operations-check" key={l.id}><input type="checkbox" checked={draft.locationIds.includes(l.id)} onChange={e=>grant("locationIds",l.id,e.target.checked)}/>{l.name}{l.isArchived?(sq?" · Arkivuar":" · Archived"):""}</label>)}
      <h3>{sq?"Pika të caktuara veçmas":"Individually assigned stations"}</h3><small>{sq?"Lejon vetëm pikën e shënuar.":"Allows only the selected station."}</small>
      {stations.filter(s=>!s.isArchived||draft.stationIds.includes(s.id)).map(s=><label className="operations-check" key={s.id}><input type="checkbox" checked={draft.stationIds.includes(s.id)} onChange={e=>grant("stationIds",s.id,e.target.checked)}/>{s.name} · {s.code}{s.isArchived?(sq?" · Arkivuar":" · Archived"):""}</label>)}
      <button className="button-primary">{busy?(sq?"Duke ruajtur…":"Saving…"):(sq?"Ruaj administratorin":"Save administrator")}</button>
    </fieldset></form>{selected&&!archived&&<button type="button" className="station-archive" disabled={busy} onClick={()=>void save(true)}>{sq?"Arkivo administratorin":"Archive administrator"}</button>}
  </section></div>;
}
