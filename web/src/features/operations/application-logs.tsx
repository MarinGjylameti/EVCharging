"use client";
import { useEffect, useState } from "react";
import type { Language } from "../../../../contracts/account";
import type { ApplicationLogPage, ApplicationLogHistory } from "../../../../contracts/operations";
import { operations, failure } from "./api";

export function ApplicationLogs({language}:{language:Language}) {
  const sq=language==="sq";
  const [page,setPage]=useState<ApplicationLogPage|ApplicationLogHistory|null>(null);
  const [mode,setMode]=useState("history");
  const [source,setSource]=useState("OCPP");
  const [draft,setDraft]=useState("");const [identity,setIdentity]=useState("");
  const [level,setLevel]=useState("");
  const [traceDraft,setTraceDraft]=useState("");const [traceId,setTraceId]=useState("");
  const [fromDraft,setFromDraft]=useState("");const [untilDraft,setUntilDraft]=useState("");
  const [dates,setDates]=useState({from:"",until:""});
  const [beforeId,setBeforeId]=useState<number|null>(null);
  const [refreshVersion,setRefreshVersion]=useState(0);
  const [busy,setBusy]=useState(false);const [error,setError]=useState("");
  useEffect(()=>{
    const controller=new AbortController();let pending=false;
    const refresh=async()=>{
      if(pending||controller.signal.aborted)return;pending=true;setBusy(true);
      const query=new URLSearchParams();if(source)query.set("source",source);if(identity&&source!=="HTTP")query.set("identity",identity);
      query.set("mode",mode);
      if(mode==="history") {
        if(level)query.set("level",level);if(traceId)query.set("traceId",traceId);
        if(dates.from)query.set("from",dates.from);if(dates.until)query.set("until",dates.until);
        if(beforeId)query.set("beforeId",String(beforeId));
      }
      try{const result=await operations<ApplicationLogPage|ApplicationLogHistory>("logs",controller.signal,`&${query}`);if(!controller.signal.aborted){setPage(result);setError("");}}
      catch(e){if(!controller.signal.aborted)setError(failure(e,sq));}
      finally{pending=false;if(!controller.signal.aborted)setBusy(false);}
    };
    void refresh();
    const timer=setInterval(()=>{if(!beforeId&&document.visibilityState==="visible")void refresh();},5000);
    return()=>{controller.abort();clearInterval(timer);};
  },[source,identity,refreshVersion,sq,mode,level,traceId,dates,beforeId]);
  const fmt=(date:string)=>new Date(date).toLocaleString(sq?"sq-AL":"en-GB");
  const history=page&&"storage" in page?page:null;
  function resetPage(){setBeforeId(null);setPage(null);}
  function exportPage(){
    if(!page)return;
    const content={exportedAt:new Date().toISOString(),scope:"Displayed page only",filters:{mode,source,identity,level,traceId,...dates,beforeId},...page};
    const url=URL.createObjectURL(new Blob([JSON.stringify(content,null,2)],{type:"application/json"}));
    const link=document.createElement("a");link.href=url;link.download=`evcharging-logs-${Date.now()}.json`;link.click();URL.revokeObjectURL(url);
  }
  return <>
    <p>{sq?"Tentativat e lidhjes, refuzimet, mesazhet OCPP dhe kërkesat HTTP shfaqen këtu. Përditësohet çdo 5 sekonda.":"Connection attempts, rejections, OCPP messages and HTTP requests appear here. Updates every 5 seconds."}</p>
    <p>{mode==="history"?(sq?`Historiku ruhet në databazë edhe pas restart/deploy${history?` për ${history.retentionDays} ditë`:""}. Ngjarjet e reja mund të vonojnë pak sekonda. Shfaqen 200 rezultate për faqe.`:`History is stored in the database across restarts/deploys${history?` for ${history.retentionDays} days`:""}. New events may take a few seconds to appear. 200 results per page.`):(sq?"Pamja live tregon 1 000 ngjarjet e fundit të kësaj instance dhe pastrohet pas restart/deploy. Historiku i ruajtur lexohet veçmas.":"Live view keeps the latest 1,000 events on this instance and resets after a restart/deploy. Saved history is available separately.")}</p>
    <form className="operations-filters" onSubmit={e=>{e.preventDefault();resetPage();setIdentity(draft.trim());setTraceId(traceDraft.trim());setDates({from:fromDraft?new Date(fromDraft).toISOString():"",until:untilDraft?new Date(untilDraft).toISOString():""});setRefreshVersion(x=>x+1);}}>
      <label>{sq?"Pamja":"View"}<select value={mode} onChange={e=>{setMode(e.target.value);resetPage();}}><option value="history">{sq?"Historiku i ruajtur":"Saved history"}</option><option value="live">{sq?"Live / kjo instancë":"Live / this instance"}</option></select></label>
      <label>{sq?"Burimi":"Source"}<select value={source} onChange={e=>{setSource(e.target.value);resetPage();}}><option value="OCPP">OCPP</option><option value="HTTP">HTTP</option><option value="Worker">Worker</option><option value="System">System</option><option value="">{sq?"Të gjitha":"All"}</option></select></label>
      <label>Charge Point ID<input maxLength={40} value={draft} disabled={source==="HTTP"} placeholder="NKR2023072805" onChange={e=>setDraft(e.target.value)}/></label>
      {mode==="history"&&<>
        <label>{sq?"Niveli":"Level"}<select value={level} onChange={e=>{setLevel(e.target.value);resetPage();}}><option value="">{sq?"Të gjitha":"All"}</option>{["Information","Warning","Error","Critical"].map(value=><option key={value}>{value}</option>)}</select></label>
        <label>Trace ID<input maxLength={100} value={traceDraft} onChange={e=>setTraceDraft(e.target.value)}/></label>
        <label>{sq?"Nga (ora lokale)":"From (local time)"}<input type="datetime-local" value={fromDraft} onChange={e=>setFromDraft(e.target.value)}/></label>
        <label>{sq?"Deri (ora lokale)":"Until (local time)"}<input type="datetime-local" min={fromDraft||undefined} value={untilDraft} onChange={e=>setUntilDraft(e.target.value)}/></label>
      </>}
      <button className="button-primary" disabled={busy}>{sq?"Filtro / Rifresko":"Filter / Refresh"}</button>
    </form>
    {error&&<p className="form-error" role="alert">{error}</p>}
    {history&&(history.storage.failureCode||history.storage.dropped>0)&&<p className="form-error" role="alert">{sq?"Ruajtja e logeve ka pasur problem. Kontrollo edhe loget e Render-it.":"Log persistence has encountered a problem. Check Render logs as well."} {history.storage.failureCode} · {sq?"Të paruajtura nga radha e plotë":"Dropped from full queue"}: {history.storage.dropped}</p>}
    <p role="status">{busy?(sq?"Duke lexuar loget…":"Loading logs…"):beforeId?(sq?"Faqe historiku; rifreskimi automatik në pauzë":"History page; automatic refresh paused"):(sq?"Përditësimi automatik aktiv":"Automatic refresh active")}{page&&"startedAt" in page&&` · ${sq?"API aktive nga":"API running since"} ${fmt(page.startedAt)} · ${page.instanceId.slice(0,8)}`}{history&&` · ${sq?"Në pritje për ruajtje":"Pending persistence"}: ${history.storage.pending}`}</p>
    <div className="operations-filters">
      <button type="button" disabled={!page?.entries.length||busy||!!error} onClick={exportPage}>{sq?"Eksporto këtë faqe (JSON)":"Export this page (JSON)"}</button>
      {mode==="history"&&<><button type="button" disabled={!beforeId||busy} onClick={resetPage}>{sq?"Më të fundit":"Latest"}</button><button type="button" disabled={!history?.nextBeforeId||busy} onClick={()=>{setBeforeId(history!.nextBeforeId);setPage(null);}}>{sq?"Më të vjetra":"Older"}</button></>}
    </div>
    <div className="operations-table-wrap"><table className="operations-table"><thead><tr><th>{sq?"Koha":"Time"}</th><th>{sq?"Burimi / Niveli":"Source / Level"}</th><th>Charge Point ID</th><th>{sq?"Mesazhi":"Message"}</th></tr></thead><tbody>
      {page?.entries.map(row=><tr key={row.eventId??`${row.instanceId}-${row.sequence}`}><td title={row.recordedAt}>{fmt(row.recordedAt)}</td><td>{row.source}<small>{row.level}</small></td><td>{row.identity??"—"}</td><td><pre>{row.message}</pre>{row.context&&<details><summary>{sq?"Detajet e gjurmimit":"Trace details"}</summary><pre>{JSON.stringify({eventId:row.eventId,instanceId:row.instanceId,...row.context},null,2)}</pre></details>}</td></tr>)}
    </tbody></table>{page&&!page.entries.length&&<p>{sq?"Nuk ka ende loge për këtë filtër. Provo rilidhjen e karikuesit; lëre ID-në bosh për të parë edhe ID të paregjistruara.":"No logs match this filter yet. Reconnect the charger; leave the ID empty to see unregistered identities too."}</p>}</div>
  </>;
}
