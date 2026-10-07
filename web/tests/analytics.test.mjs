import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { loadTypescript } from "./helpers/load-typescript.mjs";
import { analyticsDependencies, analyticsFixture, calendar } from "./helpers/analytics.mjs";

function find(node,predicate) {if(!node||typeof node!=="object")return; if(predicate(node))return node;for(const child of React.Children.toArray(node.props?.children)){const found=find(child,predicate);if(found)return found;}}
const source=name=>new URL(`../src/features/operations/${name}`,import.meta.url);
const link={default:({href,children,...props})=>React.createElement("a",{href,...props},children)};

test("analytics BFF forwards calendar dates and timezone safely and retains backend errors",async t=>{
  const {GET}=loadTypescript(new URL("../src/app/api/operations/route.ts",import.meta.url),{"@/lib/backend":{backend:async(path,init)=>{calls.push({path,init});return Response.json({code:"invalid_analytics_filter"},{status:400});},sessionCookie:"session"},"@/lib/request-body":{}});
  const calls=[];
  const response=await GET(new Request("https://web.test/api/operations?resource=analytics&period=Custom&from=2026-10-01&until=2026-10-07&timeZone=Europe%2FTirane&ignored=secret"));
  assert.equal(response.status,400);assert.equal(response.headers.get("cache-control"),"no-store");
  assert.equal(calls[0].path,"/admin/analytics?period=Custom&from=2026-10-01&until=2026-10-07&timeZone=Europe%2FTirane");
  for(const query of ["period=All","date=2026-10-07T00:00:00Z","from=../me","timeZone="+"A".repeat(101),"timeZone=Europe%0ATirane"]) assert.equal((await GET(new Request("https://web.test/api/operations?resource=analytics&"+query))).status,400);
  assert.equal(calls.length,1);
});

test("query encoding has exactly one period and the dates appropriate to that period",()=>{
  assert.equal(calendar.analyticsQuery({period:"Weekly",date:"2026-10-05",from:"ignored",until:"ignored",timeZone:"Europe/Tirane"}),"period=Weekly&timeZone=Europe%2FTirane&date=2026-10-05");
  assert.equal(calendar.analyticsQuery({period:"Custom",date:"ignored",from:"2026-10-01",until:"2026-10-07",timeZone:"Europe/Tirane"}),"period=Custom&timeZone=Europe%2FTirane&from=2026-10-01&until=2026-10-07");
  assert.equal(calendar.calendarDays("2024-02-01").filter(Boolean).length,29);
  assert.equal(calendar.moveMonth("2026-12-01",1),"2027-01-01");
});

test("selector offers only the four main periods and uses backend navigation anchors",()=>{
  const {AnalyticsPeriodSelector}=analyticsDependencies["./analytics-period-selector"];
  const initial=analyticsFixture();const changed=[];const filter={period:"Daily",timeZone:"Europe/Tirane"};
  const tree=AnalyticsPeriodSelector({filter,period:initial.period,sq:false,busy:false,onChange:value=>changed.push(value)});
  assert.equal(find(tree,node=>node.props?.["aria-label"]==="Next period").props.disabled,true);
  find(tree,node=>node.props?.["aria-label"]==="Previous period").props.onClick();
  assert.deepEqual(changed.pop(),{...filter,date:"2026-10-06"});
  for(const kind of ["Daily","Weekly","Monthly"]) {find(tree,node=>node.type==="button"&&node.props.children===kind).props.onClick();assert.deepEqual(changed.pop(),{period:kind,timeZone:"Europe/Tirane"});}
  find(tree,node=>node.type==="button"&&node.props.children==="Custom").props.onClick();
  assert.deepEqual(changed.pop(),{period:"Custom",timeZone:"Europe/Tirane",from:"2026-10-07",until:"2026-10-07"});
  const past={...initial.period,canGoNext:true,nextAnchor:"2026-10-07"};
  const previousTree=AnalyticsPeriodSelector({filter,period:past,sq:false,busy:false,onChange:value=>changed.push(value)});
  find(previousTree,node=>node.props?.["aria-label"]==="Next period").props.onClick();assert.equal(changed.pop().date,"2026-10-07");
  const monthly=renderToStaticMarkup(React.createElement(AnalyticsPeriodSelector,{filter:{period:"Monthly",timeZone:"Europe/Tirane"},period:{...initial.period,kind:"Monthly",startDate:"2026-10-01",endDate:"2026-10-31"},sq:false,busy:false,onChange(){}}));
  assert.match(monthly,/October 2026/);assert.match(monthly,/type="month"/);assert.match(monthly,/max="2026-10"/);
});

test("range picker selects a range in either direction, disables future dates and commits on Apply",()=>{
  const state=[];let cursor=0;const changes=[];
  const {AnalyticsDateRange}=loadTypescript(source("analytics-date-range.tsx"),{"./analytics-calendar":calendar,react:{...React,useState(value){const i=cursor++;if(!(i in state))state[i]=value;return [state[i],next=>{state[i]=next;}];}}});
  const render=()=>{cursor=0;return AnalyticsDateRange({from:"2026-10-01",until:"2026-10-07",today:"2026-10-07",sq:false,disabled:false,onApply:(...args)=>changes.push(args)});};
  find(render(),node=>node.type==="button"&&node.props.children==="Choose date range").props.onClick();
  find(render(),node=>node.props?.["aria-label"]==="6 October 2026").props.onClick();
  find(render(),node=>node.props?.["aria-label"]==="2 October 2026").props.onClick();
  assert.equal(changes.length,0);
  assert.equal(find(render(),node=>node.props?.["aria-label"]==="8 October 2026").props.disabled,true);
  find(render(),node=>node.type==="button"&&node.props.children==="Apply range").props.onClick();assert.deepEqual(changes,[["2026-10-02","2026-10-06"]]);
});

test("trend retains every backend daily point and marks future buckets without turning them into zero",()=>{
  const {AnalyticsTrend}=analyticsDependencies["./analytics-trend"];
  const data=analyticsFixture();const html=renderToStaticMarkup(React.createElement(AnalyticsTrend,{points:data.trend,period:data.period,metric:"energyKWh",title:"Energy trend",sq:false}));
  assert.equal((html.match(/<tr>/g)||[]).length,25);assert.match(html,/Future/);assert.match(html,/View chart data/);assert.match(html,/CEST|GMT\+2/);
  assert.match(html,/role="img"/);
});

test("changing periods cancels old requests and keeps stale snapshots out of historical panels",async()=>{
  const state=[],refs=[];let cursor=0,refCursor=0,effect,liveRefresh;const requests=[];
  const {OperationsOverview}=loadTypescript(source("operations-overview.tsx"),{...analyticsDependencies,"next/link":link,"./api":{operations:(resource,signal,query)=>new Promise(resolve=>requests.push({resource,signal,query,resolve}))},"./use-operations-live":{useOperationsLive:refresh=>{liveRefresh=refresh;return true;}},react:{...React,useState(value){const i=cursor++;if(!(i in state))state[i]=value;return [state[i],next=>{state[i]=typeof next==="function"?next(state[i]):next;}];},useRef(value){const i=refCursor++;return refs[i]??(refs[i]={current:value});},useCallback:fn=>fn,useEffect:fn=>{effect=fn;}}});
  const initial=analyticsFixture();
  const render=()=>{cursor=0;refCursor=0;return OperationsOverview({initial,language:"en",superadmin:true});};
  let tree=render();let cleanup=effect();assert.match(requests[0].query,/period=Daily/);
  const selector=find(tree,node=>node.type===analyticsDependencies["./analytics-period-selector"].AnalyticsPeriodSelector);
  selector.props.onChange({period:"Monthly",timeZone:"Europe/Tirane"});
  cleanup();tree=render();cleanup=effect();assert.equal(requests[0].signal.aborted,true);
  assert.equal(find(tree,node=>node.props?.["aria-label"]==="Period analytics"),undefined);
  assert.ok(find(tree,node=>node.props?.["aria-label"]==="Current network status"));
  const monthly={...initial,period:{...initial.period,kind:"Monthly",granularity:"Daily",startDate:"2026-10-01",endDate:"2026-10-31"},metrics:{...initial.metrics,sessions:17}};
  requests[1].resolve(monthly);await new Promise(resolve=>setImmediate(resolve));
  requests[0].resolve({...initial,metrics:{...initial.metrics,sessions:999}});await new Promise(resolve=>setImmediate(resolve));
  tree=render();assert.match(renderToStaticMarkup(tree),/17/);assert.doesNotMatch(renderToStaticMarkup(tree),/999/);
  const livePromise=liveRefresh(new AbortController().signal);assert.match(requests[2].query,/period=Monthly/);requests[2].resolve(monthly);await livePromise;cleanup();
});
