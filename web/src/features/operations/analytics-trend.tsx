import type { AnalyticsPeriod, AnalyticsTrendPoint } from "../../../../contracts/operations";
import { bucketLabel } from "./analytics-calendar";

export function AnalyticsTrend({points,period,metric,title,sq}:{points:AnalyticsTrendPoint[];period:AnalyticsPeriod;metric:"energyKWh"|"sessions";title:string;sq:boolean}) {
  const locale=sq?"sq-AL":"en-GB";
  const energy=metric==="energyKWh";
  const highest=Math.max(1,...points.map(p=>p[metric]??0));
  const max=energy?highest:Math.ceil(highest/2)*2;
  const x=(i:number)=>52+i*680/Math.max(1,points.length-1);
  const y=(value:number)=>172-value/max*138;
  const observed=points.flatMap((point,i)=>point[metric]===null?[]:[{point,i,value:point[metric]!}]);
  const path=observed.map(({i,value},index)=>`${index?"L":"M"}${x(i)},${y(value)}`).join(" ");
  const labels=Array.from(new Set([0,Math.floor((points.length-1)/2),points.length-1])).filter(i=>i>=0);
  const format=(value:number)=>new Intl.NumberFormat(locale,{maximumFractionDigits:energy?3:0}).format(value);
  return <article className="analytics-card analytics-trend"><div className="analytics-card-heading"><h2>{title}</h2><span>{period.granularity}{period.bucketSize>1?` × ${period.bucketSize}`:""}</span></div>
    <svg viewBox="0 0 760 218" role="img" aria-label={`${title}; ${period.granularity}; ${energy?"kWh":"sessions"}`}>
      {[0,.5,1].map(ratio=><g key={ratio}><line x1="52" x2="732" y1={y(max*ratio)} y2={y(max*ratio)} className="analytics-grid-line"/><text x="42" y={y(max*ratio)+4} textAnchor="end">{format(max*ratio)}</text></g>)}
      {path&&<path d={path} fill="none" className={`analytics-line ${energy?"energy":"sessions"}`}/>}
      {observed.map(({point,i,value})=><circle key={point.from} cx={x(i)} cy={y(value)} r={points.length>35?2.5:3.5} className={energy?"analytics-dot-energy":"analytics-dot-sessions"}><title>{`${bucketLabel(point.from,period,locale)}: ${format(value)} ${energy?"kWh":"sessions"}`}</title></circle>)}
      {labels.map(i=><text key={i} x={x(i)} y="202" textAnchor={i===0?"start":i===points.length-1?"end":"middle"}>{new Intl.DateTimeFormat(locale,{timeZone:period.timeZone,...(period.granularity==="Hourly"?{hour:"2-digit",minute:"2-digit"}:{day:"numeric",month:"short"})}).format(new Date(points[i].from))}</text>)}
    </svg>
    {!observed.some(p=>p.value>0)&&<p className="analytics-empty">{sq?"Nuk ka aktivitet të regjistruar në këtë periudhë.":"No recorded activity in this period."}</p>}
    <details className="analytics-chart-data"><summary>{sq?"Shiko të dhënat":"View chart data"}</summary><div className="operations-table-wrap"><table className="operations-table"><caption className="sr-only">{title}</caption><thead><tr><th scope="col">{sq?"Intervali":"Bucket starts"}</th><th scope="col">{energy?"kWh":sq?"Sesione":"Sessions"}</th></tr></thead><tbody>{points.map(point=><tr key={point.from}><td>{bucketLabel(point.from,period,locale)}</td><td>{point[metric]===null?(sq?"Në të ardhmen":"Future"):format(point[metric]!)}</td></tr>)}</tbody></table></div></details>
  </article>;
}
