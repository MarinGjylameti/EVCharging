"use client";
import { useTestMode } from "../../components/runtime-features";
import { TableScrollHint } from "../../components/table-scroll-hint";
import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import type { Language } from "../../../../contracts/account";
import type { AnalyticsFilter, OperationsAnalytics } from "../../../../contracts/operations";
import { operations } from "./api";
import { useOperationsLive } from "./use-operations-live";
import { analyticsQuery, bucketLabel, periodLabel } from "./analytics-calendar";
import { AnalyticsPeriodSelector } from "./analytics-period-selector";
import { AnalyticsTrend } from "./analytics-trend";

const labels:Record<string,[string,string]>={Available:["Të lira","Available"],Occupied:["Të zëna","Occupied"],Charging:["Në karikim","Charging"],OutOfService:["Jashtë funksionit","Out of service"],Maintenance:["Në mirëmbajtje","Maintenance"],Unknown:["Pa informacion aktual","Unknown"]};
function Comparison({value,sq}:{value:number|null;sq:boolean}) {
  return <span className="analytics-comparison">{value===null?(sq?"Pa aktivitet në periudhën e mëparshme":"No activity in previous period"):`${value>0?"+":""}${value}% ${sq?"kundrejt periudhës së mëparshme":"vs previous period"}`}</span>;
}

export function OperationsOverview({initial,language,superadmin}:{initial:OperationsAnalytics;language:Language;superadmin:boolean}) {
  const testMode=useTestMode();
  const sq=language==="sq",locale=sq?"sq-AL":"en-GB";
  const initialFilter:AnalyticsFilter={period:initial.period.kind,timeZone:initial.period.timeZone};
  if(initial.period.kind==="Custom") {initialFilter.from=initial.period.startDate;initialFilter.until=initial.period.endDate;}
  const [filter,setFilter]=useState<AnalyticsFilter>(initialFilter);
  const query=analyticsQuery(filter);
  const [snapshot,setSnapshot]=useState({data:initial,query:analyticsQuery(initialFilter)});
  const [loading,setLoading]=useState(false);
  const [error,setError]=useState("");
  const [ranking,setRanking]=useState<"locations"|"stations">("locations");
  const requestNumber=useRef(0);
  const selectedQuery=useRef(query);
  const refresh=useCallback(async(signal:AbortSignal)=>{
    const number=++requestNumber.current;
    try {
      const next=await operations<OperationsAnalytics>("analytics",signal,query);
      if(!signal.aborted&&number===requestNumber.current&&query===selectedQuery.current) {setSnapshot({data:next,query});setError("");}
    } catch(error) {
      if(!signal.aborted&&number===requestNumber.current&&query===selectedQuery.current)setError(sq?"Analiza nuk u ngarkua. Provo përsëri.":"Analytics could not be loaded. Try again.");
      throw error;
    } finally {if(!signal.aborted&&number===requestNumber.current&&query===selectedQuery.current)setLoading(false);}
  },[query,sq]);
  const live=useOperationsLive(refresh);
  useEffect(()=>{
    selectedQuery.current=query;const controller=new AbortController();setLoading(true);setError("");
    void refresh(controller.signal).catch(()=>{});
    return ()=>controller.abort();
  },[query,refresh]);
  const data=snapshot.data,period=data.period;
  const matches=snapshot.query===query;
  const busy=loading||!matches;
  const current=data.current;
  const money=(value:number)=>`${value.toFixed(2)} ${data.currency}`;
  const rows=ranking==="locations"?data.locationRanking:data.stationRanking;
  const outcomes:Record<string,string>=sq?{Completed:"Të përfunduara",Interrupted:"Të ndërprera",Unverified:"Për verifikim"}:{Completed:"Completed",Interrupted:"Interrupted",Unverified:"Needs verification"};
  const previousLabel=new Intl.DateTimeFormat(locale,{timeZone:period.timeZone,day:"numeric",month:"short",year:"numeric"});
  return <div className="workspace-body analytics-dashboard">
    <header className="analytics-header"><div><span className="eyebrow">{sq?"PANELI OPERATIV":"OPERATIONS DASHBOARD"}</span><h1>{sq?"Rrjeti i karikimit":"Charging network"}</h1></div><span className="operations-live-status" data-state={live?"active":"loading"} role="status">{live?(sq?"Përditësime aktive":"Live updates"):(sq?"Duke lidhur përditësimet…":"Connecting live updates…")}</span></header>
    <AnalyticsPeriodSelector filter={filter} period={period} sq={sq} busy={busy} onChange={setFilter}/>
    <div className="analytics-feedback" role="status">{loading?(sq?"Duke përditësuar analizën…":"Updating analytics…"):error}</div>
    {error&&<button type="button" className="button-outline" onClick={()=>{setLoading(true);void refresh(new AbortController().signal).catch(()=>{});}}>{sq?"Provo përsëri":"Retry"}</button>}
    {matches&&<section className="analytics-history" aria-busy={loading} aria-label={sq?"Analiza e periudhës":"Period analytics"}>
      <div className="analytics-section-heading"><h2>{sq?"Analiza e periudhës":"Period analytics"}</h2><span>{periodLabel(period,locale)}</span></div>
      <div className="analytics-kpis">
        <article><span>{sq?"Sesione të nisura":"Sessions started"}</span><strong>{data.metrics.sessions}</strong><Comparison value={data.comparison.sessionsPercent} sq={sq}/></article>
        <article><span>{sq?"Energji nga sesionet e përfunduara":"Energy from completed sessions"}</span><strong>{data.metrics.energyKWh.toFixed(3)} <small>kWh</small></strong><Comparison value={data.comparison.energyPercent} sq={sq}/></article>
        <article><span>{sq?"Kosto e llogaritur":"Calculated cost"}</span><strong>{money(data.metrics.calculatedCost)}</strong><Comparison value={data.comparison.costPercent} sq={sq}/></article>
        <article><span>{sq?"Sesione të përfunduara":"Completed sessions"}</span><strong>{data.metrics.completedSessions}</strong><small>{sq?"Përfunduan në këtë periudhë":"Ended within this period"}</small></article>
      </div>
      <p className="analytics-note">{sq?"Krahasimi me":"Compared with"} {previousLabel.format(new Date(period.previousFrom))} – {previousLabel.format(new Date(new Date(period.previousUntil).getTime()-1))}{period.isPartial?` · ${sq?"Periudha aktuale është ende në vazhdim; krahasimi përdor periudhën e plotë të mëparshme.":"The current period is still in progress; comparison uses the full previous period."}`:""}</p>
      <div className="analytics-chart-grid"><AnalyticsTrend points={data.trend} period={period} metric="energyKWh" title={sq?"Trendi i energjisë":"Energy trend"} sq={sq}/><AnalyticsTrend points={data.trend} period={period} metric="sessions" title={sq?"Trendi i sesioneve":"Sessions trend"} sq={sq}/></div>
      <div className="analytics-details-grid">
        <article className="analytics-card analytics-ranking"><div className="analytics-card-heading"><h2>{sq?"Renditja sipas energjisë":"Ranking by energy"}</h2><div className="analytics-small-tabs" role="group" aria-label={sq?"Lloji i renditjes":"Ranking type"}><button type="button" aria-pressed={ranking==="locations"} onClick={()=>setRanking("locations")}>{sq?"Vendndodhje":"Locations"}</button><button type="button" aria-pressed={ranking==="stations"} onClick={()=>setRanking("stations")}>{sq?"Pika":"Stations"}</button></div></div>
          {rows.length?<><TableScrollHint sq={sq}/><div className="operations-table-wrap" tabIndex={0} role="region" aria-label={sq?"Renditja sipas aktivitetit":"Activity ranking"}><table className="operations-table"><thead><tr><th scope="col">{sq?"Emri":"Name"}</th><th scope="col">{sq?"Sesione të nisura":"Sessions started"}</th><th scope="col">kWh</th><th scope="col">{sq?"Kosto":"Cost"}</th></tr></thead><tbody>{rows.map((row,i)=><tr key={row.id??"unassigned"}><td><span className="analytics-rank-number">{i+1}</span>{row.name==="Unassigned"?(sq?"Pa vendndodhje":"Unassigned"):row.name}{row.code&&<small>{row.code}</small>}</td><td>{row.sessions}</td><td>{row.energyKWh.toFixed(3)}</td><td>{money(row.calculatedCost)}</td></tr>)}</tbody></table></div></>:<p className="analytics-empty">{sq?"Nuk ka të dhëna për këtë periudhë.":"No data for this period."}</p>}
          <p className="analytics-note">{sq?"10 rezultatet e para në periudhën e zgjedhur":"Top 10 within the selected period"}</p>
        </article>
        <article className="analytics-card"><h2>{sq?"Rezultatet e sesioneve":"Session outcomes"}</h2><p className="analytics-note">{sq?"Sesione që përfunduan në periudhën e zgjedhur":"Sessions that ended within the selected period"}</p>{data.outcomes.length?<ul className="analytics-outcomes">{data.outcomes.map(outcome=><li key={outcome.kind}><div><span>{outcomes[outcome.kind]??outcome.kind}</span><strong>{outcome.sessions} <small>({outcome.percent}%)</small></strong></div><progress max={100} value={outcome.percent} aria-label={outcomes[outcome.kind]??outcome.kind}/></li>)}</ul>:<p className="analytics-empty">{sq?"Nuk ka sesione të përfunduara.":"No completed sessions."}</p>}
          <div className="analytics-peak"><h3>{sq?"Intervali me më shumë nisje":"Busiest start interval"}</h3>{data.peakUsage?<><strong>{data.peakUsage.sessions} {sq?"sesione":"sessions"}</strong><span>{bucketLabel(data.peakUsage.from,period,locale)} – {bucketLabel(data.peakUsage.until,period,locale)}</span></>:<p className="analytics-empty">{sq?"Nuk ka nisje në këtë periudhë.":"No session starts in this period."}</p>}</div>
        </article>
      </div>
      <p className="analytics-note analytics-attribution">{sq?"Sesionet numërohen sipas datës së nisjes. Energjia dhe kostoja caktohen në datën e përfundimit të sesionit; nuk përfaqësojnë matje të shpërndara sipas orës. Kostoja llogaritet nga tarifa e ruajtur e çdo sesioni.":"Sessions are counted by start date. Energy and cost are assigned to the session’s completion date; they do not represent hourly meter readings. Cost uses each session’s saved tariff."}{testMode && (sq ? " Përfshihen edhe testet demo." : " Demo tests are included.")}{data.metrics.unverifiedEnergySessions>0&&` ${data.metrics.unverifiedEnergySessions} ${sq?"sesione të përfunduara pa energji të verifikuar; nuk përfshihen në energji dhe kosto.":"completed sessions have no verified energy; excluded from energy and cost."}`}</p>
    </section>}
    <section className="analytics-current" aria-label={sq?"Gjendja aktuale":"Current network status"}><div className="analytics-section-heading"><h2>{sq?"Gjendja tani":"Network now"}</h2><span>{sq?"Vlera aktuale, të pavarura nga periudha":"Current values, independent of selected period"}</span></div>
      <div className="operations-stats"><article><span>{sq?"Karikime aktive tani":"Active sessions now"}</span><strong>{current.activeSessions}</strong></article><article><span>{sq?"Pika të lira tani":"Available chargers now"}</span><strong>{current.statusCounts.Available}</strong></article><article><span>{sq?"Pika aktive tani":"Active stations now"}</span><strong>{current.stations}</strong></article><article><span>{sq?"Vendndodhje aktive tani":"Active locations now"}</span><strong>{current.locations}</strong></article>{superadmin&&<><article><span>{sq?"Administratorë aktivë":"Active administrators"}</span><strong>{current.administrators}</strong></article><article><span>{sq?"Drejtues aktivë":"Active drivers"}</span><strong>{current.drivers}</strong></article></>}</div>
      <div className="analytics-statuses">{Object.entries(current.statusCounts).map(([key,value])=><span key={key}>{labels[key]?.[sq?0:1]??key} <strong>{value}</strong></span>)}</div><p className="analytics-note">{sq?"Përditësuar":"Updated"} {new Intl.DateTimeFormat(locale,{timeZone:period.timeZone,dateStyle:"medium",timeStyle:"short"}).format(new Date(current.updatedAt))} · {period.timeZone}</p>
    </section>
    <div className="operations-actions"><Link href="/dashboard/stations" className="button-primary inline-link">{sq?"Menaxho pikat":"Manage stations"}</Link><Link href="/dashboard/sessions" className="button-outline inline-link">{sq?"Monitoro karikimet":"Monitor sessions"}</Link><Link href="/dashboard/audit" className="button-outline inline-link">{sq?"Aktiviteti dhe auditimi":"Activity and audit"}</Link></div>
  </div>;
}
