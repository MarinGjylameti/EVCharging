import type { AnalyticsFilter, AnalyticsPeriod } from "../../../../contracts/operations";

export function analyticsQuery(filter:AnalyticsFilter) {
  const query=new URLSearchParams({period:filter.period,timeZone:filter.timeZone});
  if(filter.period==="Custom") {if(filter.from)query.set("from",filter.from);if(filter.until)query.set("until",filter.until);}
  else if(filter.date)query.set("date",filter.date);
  return query.toString();
}
// UTC here is only a stable representation of a calendar date for the picker.
// The API converts selected dates into authoritative network-time-zone boundaries.
export function calendarDate(value:string) {return new Date(value+"T00:00:00Z");}
export function dateValue(date:Date) {return date.toISOString().slice(0,10);}
export function moveMonth(value:string,months:number) {
  const date=calendarDate(value);date.setUTCDate(1);date.setUTCMonth(date.getUTCMonth()+months);return dateValue(date);
}
export function calendarDays(month:string):(string|null)[] {
  const first=calendarDate(month);const offset=(first.getUTCDay()+6)%7;
  const last=new Date(first);last.setUTCMonth(last.getUTCMonth()+1);last.setUTCDate(0);
  return [...Array<string|null>(offset).fill(null),...Array.from({length:last.getUTCDate()},(_,i)=>{const date=new Date(first);date.setUTCDate(i+1);return dateValue(date);})];
}
export function formatCalendar(value:string,locale:string,monthOnly=false) {
  return new Intl.DateTimeFormat(locale,{timeZone:"UTC",...(monthOnly?{month:"long",year:"numeric"}:{day:"numeric",month:"long",year:"numeric"})}).format(calendarDate(value));
}
export function periodLabel(period:AnalyticsPeriod,locale:string) {
  if(period.kind==="Monthly")return formatCalendar(period.startDate,locale,true);
  if(period.startDate===period.endDate)return formatCalendar(period.startDate,locale);
  return `${formatCalendar(period.startDate,locale)} – ${formatCalendar(period.endDate,locale)}`;
}
export function bucketLabel(from:string,period:AnalyticsPeriod,locale:string) {
  return new Intl.DateTimeFormat(locale,{timeZone:period.timeZone,day:"numeric",month:"short",...(period.granularity==="Hourly"?{hour:"2-digit",minute:"2-digit",timeZoneName:"short"}:{year:"numeric"})}).format(new Date(from));
}
