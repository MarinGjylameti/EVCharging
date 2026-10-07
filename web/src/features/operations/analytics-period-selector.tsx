"use client";
import type { AnalyticsFilter, AnalyticsKind, AnalyticsPeriod } from "../../../../contracts/operations";
import { AnalyticsDateRange } from "./analytics-date-range";
import { periodLabel } from "./analytics-calendar";

export function AnalyticsPeriodSelector({filter,period,sq,busy,onChange}:{filter:AnalyticsFilter;period:AnalyticsPeriod;sq:boolean;busy:boolean;onChange:(filter:AnalyticsFilter)=>void}) {
  const locale=sq?"sq-AL":"en-GB";
  const select=(kind:AnalyticsKind)=>onChange(kind==="Custom"?{period:kind,timeZone:period.timeZone,from:period.startDate,until:period.endDate<period.today?period.endDate:period.today}:{period:kind,timeZone:period.timeZone});
  return <section className="analytics-period-control" aria-label={sq?"Periudha e analizës":"Analytics period"}>
    <div className="analytics-period-tabs" role="group" aria-label={sq?"Zgjidh periudhën":"Select period"}>{(["Daily","Weekly","Monthly","Custom"] as const).map(kind=><button type="button" key={kind} aria-pressed={filter.period===kind} onClick={()=>select(kind)}>{kind}</button>)}</div>
    <div className="analytics-period-navigation">
      {filter.period!=="Custom"&&<button type="button" className="analytics-arrow" aria-label={sq?"Periudha e mëparshme":"Previous period"} disabled={busy} onClick={()=>onChange({period:filter.period,timeZone:period.timeZone,date:period.previousAnchor})}>←</button>}
      <strong>{periodLabel(period,locale)}</strong>
      {filter.period!=="Custom"&&<button type="button" className="analytics-arrow" aria-label={sq?"Periudha tjetër":"Next period"} disabled={busy||!period.canGoNext} onClick={()=>onChange({period:filter.period,timeZone:period.timeZone,date:period.nextAnchor})}>→</button>}
      {filter.period==="Daily"&&<label className="analytics-anchor"><span className="sr-only">{sq?"Data e zgjedhur":"Selected day"}</span><input aria-label={sq?"Data e zgjedhur":"Selected day"} type="date" max={period.today} value={filter.date??period.today} onChange={e=>{if(e.target.value)onChange({...filter,date:e.target.value});}}/></label>}
      {filter.period==="Weekly"&&<label className="analytics-anchor"><span className="sr-only">{sq?"Ditë brenda javës":"Day in selected week"}</span><input aria-label={sq?"Ditë brenda javës":"Day in selected week"} type="date" max={period.today} value={filter.date??period.today} onChange={e=>{if(e.target.value)onChange({...filter,date:e.target.value});}}/></label>}
      {filter.period==="Monthly"&&<label className="analytics-anchor"><span className="sr-only">{sq?"Muaji i zgjedhur":"Selected month"}</span><input aria-label={sq?"Muaji i zgjedhur":"Selected month"} type="month" max={period.today.slice(0,7)} value={(filter.date??period.today).slice(0,7)} onChange={e=>{if(e.target.value)onChange({...filter,date:e.target.value+"-01"});}}/></label>}
      {filter.period==="Custom"&&<AnalyticsDateRange from={filter.from??period.startDate} until={filter.until??period.today} today={period.today} sq={sq} disabled={busy} onApply={(from,until)=>onChange({period:"Custom",timeZone:period.timeZone,from,until})}/>}
    </div>
    <p className="analytics-period-meta">{period.timeZone} · {sq?"Java: e hënë–e diel":"Week: Monday–Sunday"}{period.isPartial?` · ${sq?"Periudha aktuale deri tani":"Current period so far"}`:""}</p>
  </section>;
}
