"use client";
import { useEffect, useState } from "react";
import type { Language } from "../../../../contracts/account";
import type { ApplicationLogPage } from "../../../../contracts/operations";
import { operations, failure } from "./api";

export function ApplicationLogs({language}:{language:Language}) {
  const sq=language==="sq";
  const [page,setPage]=useState<ApplicationLogPage|null>(null);
  const [source,setSource]=useState("OCPP");
  const [draft,setDraft]=useState("");const [identity,setIdentity]=useState("");
  const [refreshVersion,setRefreshVersion]=useState(0);
  const [busy,setBusy]=useState(false);const [error,setError]=useState("");
  useEffect(()=>{
    const controller=new AbortController();let pending=false;
    const refresh=async()=>{
      if(pending||controller.signal.aborted)return;pending=true;setBusy(true);
      const query=new URLSearchParams();if(source)query.set("source",source);if(identity&&source!=="HTTP")query.set("identity",identity);
      try{const result=await operations<ApplicationLogPage>("logs",controller.signal,`&${query}`);if(!controller.signal.aborted){setPage(result);setError("");}}
      catch(e){if(!controller.signal.aborted)setError(failure(e,sq));}
      finally{pending=false;if(!controller.signal.aborted)setBusy(false);}
    };
    void refresh();
    const timer=setInterval(()=>{if(document.visibilityState==="visible")void refresh();},5000);
    return()=>{controller.abort();clearInterval(timer);};
  },[source,identity,refreshVersion,sq]);
  const fmt=(date:string)=>new Date(date).toLocaleString(sq?"sq-AL":"en-GB");
  return <>
    <p>{sq?"Tentativat e lidhjes, refuzimet, mesazhet OCPP dhe kërkesat HTTP shfaqen këtu. Përditësohet çdo 5 sekonda.":"Connection attempts, rejections, OCPP messages and HTTP requests appear here. Updates every 5 seconds."}</p>
    <p>{sq?"Loget mbajnë 1 000 ngjarjet e fundit të kësaj instance API dhe pastrohen pas restart/deploy. Shfaqen deri në 200 rezultate; historiku i karikimeve ruhet veçmas.":"Logs keep the latest 1,000 events on this API instance and reset after a restart/deploy. Up to 200 results are shown; charging history is stored separately."}</p>
    <form className="operations-filters" onSubmit={e=>{e.preventDefault();setIdentity(draft.trim());setRefreshVersion(x=>x+1);}}>
      <label>{sq?"Burimi":"Source"}<select value={source} onChange={e=>{setSource(e.target.value);setPage(null);}}><option value="OCPP">OCPP</option><option value="HTTP">HTTP</option><option value="">{sq?"Të gjitha":"All"}</option></select></label>
      <label>Charge Point ID<input maxLength={40} value={draft} disabled={source==="HTTP"} placeholder="NKR2023072805" onChange={e=>setDraft(e.target.value)}/></label>
      <button className="button-primary" disabled={busy}>{sq?"Filtro / Rifresko":"Filter / Refresh"}</button>
    </form>
    {error&&<p className="form-error" role="alert">{error}</p>}
    <p role="status">{busy?(sq?"Duke lexuar loget…":"Loading logs…"):(sq?"Përditësimi automatik aktiv":"Automatic refresh active")}{page&&` · ${sq?"API aktive nga":"API running since"} ${fmt(page.startedAt)} · ${page.instanceId.slice(0,8)}`}</p>
    <div className="operations-table-wrap"><table className="operations-table"><thead><tr><th>{sq?"Koha":"Time"}</th><th>{sq?"Burimi / Niveli":"Source / Level"}</th><th>Charge Point ID</th><th>{sq?"Mesazhi":"Message"}</th></tr></thead><tbody>
      {page?.entries.map(row=><tr key={`${page.instanceId}-${row.sequence}`}><td>{fmt(row.recordedAt)}</td><td>{row.source}<small>{row.level}</small></td><td>{row.identity??"—"}</td><td><pre>{row.message}</pre></td></tr>)}
    </tbody></table>{page&&!page.entries.length&&<p>{sq?"Nuk ka ende loge për këtë filtër. Provo rilidhjen e karikuesit; lëre ID-në bosh për të parë edhe ID të paregjistruara.":"No logs match this filter yet. Reconnect the charger; leave the ID empty to see unregistered identities too."}</p>}</div>
  </>;
}
