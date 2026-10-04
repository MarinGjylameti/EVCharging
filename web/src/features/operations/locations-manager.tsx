"use client";
import { useState } from "react";
import type { Language } from "../../../../contracts/account";
import type { Location } from "../../../../contracts/operations";
import { failure, mutate, operations } from "./api";
import { useOperationsLive } from "./use-operations-live";

const blank={name:"",city:"",address:"",latitude:41.3275,longitude:19.8187,version:0};
export function LocationsManager({initial,language,canEdit}:{initial:Location[];language:Language;canEdit:boolean}){
  const sq=language==="sq";const [items,setItems]=useState(initial);const [selected,setSelected]=useState<string|null>(null);const [draft,setDraft]=useState(blank);const [busy,setBusy]=useState(false);const [error,setError]=useState("");const [message,setMessage]=useState("");
  useOperationsLive(async signal=>{const rows=await operations<Location[]>("locations",signal);if(!signal.aborted)setItems(rows);});
  function select(row:Location){setSelected(row.id);setDraft({name:row.name,city:row.city,address:row.address,latitude:row.latitude,longitude:row.longitude,version:row.version});setError("");setMessage("");}
  async function save(archive=false){
    if(busy||!canEdit)return;if(archive&&!window.confirm(sq?"Arkivo vendndodhjen?":"Archive location?"))return;setBusy(true);setError("");setMessage("");
    try{const row=await mutate<Location>("locations",archive?"archive":selected?"update":"create",archive?{version:draft.version}:draft,selected??undefined);setItems(previous=>[...previous.filter(x=>x.id!==row.id),row]);select(row);setMessage(sq?"Vendndodhja u ruajt.":"Location saved.");}
    catch(e){setError(failure(e,sq));}finally{setBusy(false);}
  }
  const archived=items.find(x=>x.id===selected)?.isArchived;
  return <div className="operations-grid"><section className="operations-card"><h2>{sq?"Vendndodhjet":"Locations"}</h2>{canEdit&&<button className="button-outline" type="button" onClick={()=>{setSelected(null);setDraft(blank);setError("");setMessage("");}}>{sq?"Shto vendndodhje":"Add location"}</button>}
    {items.map(row=><button key={row.id} type="button" className="station-admin-row" aria-pressed={row.id===selected} onClick={()=>select(row)}><span><strong>{row.name}</strong><small>{row.city} · {row.address}{row.isArchived?(sq?" · Arkivuar":" · Archived"):""}</small></span></button>)}{!items.length&&<p>{sq?"Nuk ka vendndodhje të caktuara.":"No assigned locations."}</p>}
  </section><section className="operations-card"><h2>{selected?(sq?"Redakto vendndodhjen":"Edit location"):(sq?"Vendndodhje e re":"New location")}</h2>
    {error&&<p role="alert" className="form-error">{error}</p>}{message&&<p role="status" className="form-success">{message}</p>}
    <form onSubmit={e=>{e.preventDefault();void save();}}><fieldset className="station-form-fields" disabled={busy||!canEdit||archived}>
      <label>{sq?"Emri":"Name"}<input required minLength={2} maxLength={120} value={draft.name} onChange={e=>setDraft({...draft,name:e.target.value})}/></label><label>{sq?"Qyteti":"City"}<input required minLength={2} maxLength={80} value={draft.city} onChange={e=>setDraft({...draft,city:e.target.value})}/></label><label>{sq?"Adresa":"Address"}<input required minLength={2} maxLength={200} value={draft.address} onChange={e=>setDraft({...draft,address:e.target.value})}/></label>
      <div className="station-form-pair"><label>Latitude<input required type="number" step="any" min={-90} max={90} value={draft.latitude} onChange={e=>setDraft({...draft,latitude:Number(e.target.value)})}/></label><label>Longitude<input required type="number" step="any" min={-180} max={180} value={draft.longitude} onChange={e=>setDraft({...draft,longitude:Number(e.target.value)})}/></label></div>
      {canEdit&&<button className="button-primary">{busy?(sq?"Duke ruajtur…":"Saving…"):(sq?"Ruaj vendndodhjen":"Save location")}</button>}
    </fieldset></form>{canEdit&&selected&&!archived&&<button type="button" className="station-archive" disabled={busy} onClick={()=>void save(true)}>{sq?"Arkivo vendndodhjen":"Archive location"}</button>}
    <p>{sq?"Një vendndodhje mund të ketë shumë pika. Zhvendos ose arkivo pikat aktive përpara se ta arkivosh.":"A location can contain multiple stations. Move or archive active stations before archiving it."}</p>
  </section></div>;
}
