"use client";
import { useState } from "react";
import Link from "next/link";
import type { ChargingHistory, Language } from "../../../../contracts/account";

export function HistoryView({history, language}: {history:ChargingHistory[];language:Language}) {
  const sq=language==="sq";
  const [selectedId,setSelectedId]=useState(history[0]?.id ?? "");
  const selected=history.find(x=>x.id===selectedId) ?? history[0];
  const totalEnergy=history.reduce((sum,x)=>sum+x.energyKWh,0);
  const totalCost=history.reduce((sum,x)=>sum+x.totalCost,0);
  const number=(value:number,digits=1)=>new Intl.NumberFormat(sq?"sq-AL":"en-GB",{minimumFractionDigits:digits,maximumFractionDigits:digits}).format(value);
  const date=(value:string)=>new Intl.DateTimeFormat(sq?"sq-AL":"en-GB",{dateStyle:"medium",timeStyle:"short",timeZone:"Europe/Tirane"}).format(new Date(value));
  return <div className="driver-content">
    {history.some(x=>x.isDemo)&&<div className="demo-banner" role="note"><span aria-hidden="true">✦</span>
      {sq?"Historik demo: këto karikime janë krijuar vetëm për testimin e pamjes.":"Demo history: these charges exist only for the preview."}</div>}
    <div className="driver-welcome"><div><span className="driver-eyebrow">{sq?"SEANCAT E MIA":"MY SESSIONS"}</span>
      <h1>{sq?"Historiku i karikimeve":"Charging history"}</h1>
      <p>{sq?"Çdo karikim, energjia dhe kostoja e llogaritur në një vend.":"Every charge, energy and calculated cost in one place."}</p></div>
      <Link href="/driver" className="driver-history-link">← {sq?"Kthehu te harta":"Back to map"}</Link></div>
    <div className="driver-kpis">
      <article><span>{sq?"KARIKIME":"CHARGES"}</span><strong>{history.length}</strong><small>{sq?"seanca të përfunduara":"completed sessions"}</small></article>
      <article><span>{sq?"ENERGJI TOTALE":"TOTAL ENERGY"}</span><strong>{number(totalEnergy)}<small className="unit"> kWh</small></strong><small>{sq?"nga seancat e tua":"from your sessions"}</small></article>
      <article><span>{sq?"KOSTO E LLOGARITUR":"CALCULATED COST"}</span><strong>€{number(totalCost,2)}</strong><small>{sq?"pa pagesë elektronike":"no electronic payment"}</small></article>
    </div>
    {history.length ? <div className="history-layout">
      <section className="history-list" aria-label={sq?"Seancat e karikimit":"Charging sessions"}>
        <div className="history-list-head"><span className="driver-eyebrow">{sq?"AKTIVITETI I FUNDIT":"RECENT ACTIVITY"}</span>
          <h2>{sq?"Karikimet e përfunduara":"Completed charges"}</h2></div>
        {history.map(item=><button key={item.id} type="button" className="history-row" aria-pressed={selected?.id===item.id}
          onClick={()=>setSelectedId(item.id)}>
          <span className="history-row-icon" aria-hidden="true">ϟ</span>
          <span className="history-row-main"><strong>{item.stationName}</strong><small>{item.city} · {date(item.startedAt)}</small></span>
          <span className="history-row-values"><strong>{number(item.energyKWh)} kWh</strong><small>€{number(item.totalCost,2)}</small></span>
          <span className="history-row-chevron" aria-hidden="true">›</span>
        </button>)}
      </section>
      {selected&&<aside className="history-detail" aria-label={sq?"Detajet e karikimit":"Charging details"}>
        <span className="driver-eyebrow">{sq?"DETAJET E SEANCËS":"SESSION DETAILS"}</span>
        <div className="history-detail-icon" aria-hidden="true">ϟ</div>
        <h2>{selected.stationName}</h2><p>{selected.city} · {selected.stationCode}</p>
        <div className="history-detail-line"><span>{sq?"Fillimi":"Started"}</span><strong>{date(selected.startedAt)}</strong></div>
        <div className="history-detail-line"><span>{sq?"Përfundimi":"Finished"}</span><strong>{date(selected.endedAt)}</strong></div>
        <div className="history-detail-line"><span>{sq?"Kohëzgjatja":"Duration"}</span><strong>{Math.round((new Date(selected.endedAt).getTime()-new Date(selected.startedAt).getTime())/60000)} min</strong></div>
        <div className="history-detail-line"><span>{sq?"Energjia":"Energy"}</span><strong>{number(selected.energyKWh)} kWh</strong></div>
        <div className="history-detail-line"><span>{sq?"Çmimi për kWh":"Price per kWh"}</span><strong>€{number(selected.unitPriceSnapshot,2)}</strong></div>
        <div className="history-total"><span>{sq?"Kosto e llogaritur":"Calculated cost"}</span><strong>€{number(selected.totalCost,2)}</strong></div>
        <p className="history-note">{sq?"Kjo shumë është informuese. Nuk është kryer pagesë.":"This amount is informational. No payment was made."}</p>
      </aside>}
    </div> : <section className="empty-state"><h2>{sq?"Ende asnjë karikim":"No charges yet"}</h2><p>{sq?"Kur një seancë të përfundojë, do ta shohësh këtu.":"Completed sessions will appear here."}</p></section>}
  </div>;
}
