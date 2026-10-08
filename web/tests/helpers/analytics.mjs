import { loadTypescript } from "./load-typescript.mjs";

const path=name=>new URL(`../../src/features/operations/${name}`,import.meta.url);
export const calendar=loadTypescript(path("analytics-calendar.ts"));
const dateRange=loadTypescript(path("analytics-date-range.tsx"),{"./analytics-calendar":calendar});
const selector=loadTypescript(path("analytics-period-selector.tsx"),{"./analytics-calendar":calendar,"./analytics-date-range":dateRange});
const trend=loadTypescript(path("analytics-trend.tsx"),{"./analytics-calendar":calendar});
const tableHint=loadTypescript(new URL("../../src/components/table-scroll-hint.tsx",import.meta.url));
export const analyticsDependencies={"./analytics-calendar":calendar,"./analytics-period-selector":selector,"./analytics-trend":trend,"../../components/table-scroll-hint":tableHint};
export function analyticsFixture() {
  const metrics={sessions:3,completedSessions:2,energyKWh:4,calculatedCost:1.4,unverifiedEnergySessions:0};
  return {
    period:{kind:"Daily",timeZone:"Europe/Tirane",today:"2026-10-07",startDate:"2026-10-07",endDate:"2026-10-07",from:"2026-10-06T22:00:00Z",until:"2026-10-07T22:00:00Z",effectiveUntil:"2026-10-07T10:00:00Z",previousFrom:"2026-10-05T22:00:00Z",previousUntil:"2026-10-06T22:00:00Z",previousAnchor:"2026-10-06",nextAnchor:"2026-10-08",canGoNext:false,granularity:"Hourly",bucketSize:1,isPartial:true},
    current:{stations:3,locations:1,administrators:2,drivers:4,statusCounts:{Available:1,Occupied:0,Charging:1,OutOfService:1,Maintenance:0,Unknown:0},activeSessions:1,updatedAt:"2026-10-07T10:00:00Z"},
    metrics,comparison:{previous:{...metrics,sessions:2},sessionsPercent:50,energyPercent:0,costPercent:0},
    trend:Array.from({length:24},(_,i)=>({from:new Date(Date.parse("2026-10-06T22:00:00Z")+i*3600000).toISOString(),until:new Date(Date.parse("2026-10-06T22:00:00Z")+(i+1)*3600000).toISOString(),sessions:i<12?(i===0?3:0):null,completedSessions:i<12?(i===0?2:0):null,energyKWh:i<12?(i===0?4:0):null,calculatedCost:i<12?(i===0?1.4:0):null})),
    stationRanking:[{id:"station-1",name:"Central station",code:"CP-001",...metrics}],locationRanking:[{id:"location-1",name:"Central depot",code:null,...metrics}],outcomes:[{kind:"Completed",sessions:2,percent:100}],peakUsage:{from:"2026-10-06T22:00:00Z",until:"2026-10-06T23:00:00Z",sessions:3},currency:"EUR",attribution:"SessionStartsAndCompletedSessionEnergy",updatedAt:"2026-10-07T10:00:00Z"
  };
}
