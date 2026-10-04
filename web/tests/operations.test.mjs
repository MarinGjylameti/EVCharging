import test from "node:test";
import assert from "node:assert/strict";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { loadTypescript } from "./helpers/load-typescript.mjs";

const id="14eedf70-4774-4b4d-808e-8f91dbbc13ce";
function bff(t,token="private-session"){
  const previous={fetch:globalThis.fetch,api:process.env.EVCHARGING_API_BASE_URL,origin:process.env.EVCHARGING_WEB_ORIGIN};
  process.env.EVCHARGING_API_BASE_URL="https://api.example.test";process.env.EVCHARGING_WEB_ORIGIN="https://web.example.test";
  t.after(()=>{globalThis.fetch=previous.fetch;for(const [name,value]of [["EVCHARGING_API_BASE_URL",previous.api],["EVCHARGING_WEB_ORIGIN",previous.origin]]){if(value===undefined)delete process.env[name];else process.env[name]=value;}});
  const headers={"next/headers":{cookies:async()=>({get:()=>token?{value:token}:undefined})}};
  const backend=loadTypescript(new URL("../src/lib/backend.ts",import.meta.url),{...headers,react:{cache:fn=>fn}});
  const body=loadTypescript(new URL("../src/lib/request-body.ts",import.meta.url));
  return path=>loadTypescript(new URL("../src/app/api/"+path+"/route.ts",import.meta.url),{...headers,"@/lib/backend":backend,"@/lib/request-body":body});
}
const request=(body,origin="https://web.example.test")=>new Request("https://web.example.test/api/operations",{method:"POST",headers:{"Content-Type":"application/json",Origin:origin},body:JSON.stringify(body)});
test("device provisioning forwards credentials only through the authenticated same-origin BFF and disables caching",async t=>{
  const route=bff(t)("operations");let calls=0;
  globalThis.fetch=async(url,init)=>{calls++;assert.equal(url,"https://api.example.test/api/v1/admin/stations/"+id+"/device");assert.equal(init.headers.get("Authorization"),"Bearer private-session");assert.equal(init.method,"POST");return Response.json({username:"CP-001",password:"device-secret"});};
  const response=await route.POST(request({resource:"device",operation:"provision",id,input:{identity:"CP-001",serialNumber:"123",protocol:"1.6J",authorizedTags:["CARD"]}}));
  assert.equal(response.status,200);assert.equal(response.headers.get("cache-control"),"no-store");assert.equal((await response.json()).password,"device-secret");assert.equal(calls,1);
});
test("admin BFF rejects cross-origin writes, arbitrary resources, traversal, missing IDs and invalid operations",async t=>{
  const route=bff(t)("operations");globalThis.fetch=async()=>assert.fail("Invalid request reached upstream");
  const input={resource:"locations",operation:"create",input:{name:"Site"}};
  assert.equal((await route.POST(request(input,"https://attacker.test"))).status,403);
  for(const body of [{...input,resource:"../me"},{...input,operation:"delete"},{...input,operation:"update",id:"../account"},{resource:"device",operation:"provision",input:{identity:"CP-001"}},{...input,input:[]}])assert.equal((await route.POST(request(body))).status,400);
  for(const query of ["resource=__proto__","resource=../me","resource=device&id=../me","resource=sessions&stationId=invalid"])assert.equal((await route.GET(new Request("https://web.example.test/api/operations?"+query))).status,400);
});
test("session filters, resource reads and device control retain backend authorization and conflict responses",async t=>{
  const route=bff(t)("operations");let seen;
  globalThis.fetch=async(url,init)=>{seen={url,init};return Response.json({code:"version_conflict"},{status:412});};
  const response=await route.POST(request({resource:"device",operation:"control",id,input:{version:1,enabled:false}}));
  assert.equal(response.status,412);assert.equal(seen.init.method,"PATCH");assert.equal(seen.url,"https://api.example.test/api/v1/admin/stations/"+id+"/device");
  globalThis.fetch=async(url,init)=>{seen={url,init};return Response.json({code:"access_denied"},{status:403});};
  const rows=await route.GET(new Request("https://web.example.test/api/operations?resource=sessions&stationId="+id+"&driver=Arta%20Test&page=2&ignored=secret"));
  assert.equal(rows.status,403);assert.match(seen.url,/driver=Arta\+Test/);assert.doesNotMatch(seen.url,/ignored/);assert.equal(seen.init.cache,"no-store");
});
test("operations SSE is streamed before upstream completion and propagates cancellation",async t=>{
  const route=bff(t)("operations/events");const controller=new AbortController();let signal;
  globalThis.fetch=async(url,init)=>{assert.equal(url,"https://api.example.test/api/v1/admin/events");signal=init.signal;return new Response(new ReadableStream({start(c){c.enqueue(new TextEncoder().encode("event: ready\ndata: {}\n\n"));}}));};
  const response=await route.GET(new Request("https://web.example.test/api/operations/events",{signal:controller.signal}));
  assert.equal(response.headers.get("content-type"),"text/event-stream");assert.equal(new TextDecoder().decode((await response.body.getReader().read()).value),"event: ready\ndata: {}\n\n");controller.abort();assert.equal(signal.aborted,true);
});
test("an anonymous operations SSE subscriber cannot contact the backend",async t=>{
  const route=bff(t,null)("operations/events");globalThis.fetch=async()=>assert.fail("Anonymous upstream");
  assert.equal((await route.GET(new Request("https://web.example.test/api/operations/events"))).status,401);
});
function component(name,dependencies={}){
  return loadTypescript(new URL("../src/features/operations/"+name+".tsx",import.meta.url),{react:{...React,useEffect:()=>{},useState:initial=>[initial,()=>{}]},"./api":{},"./use-operations-live":{useOperationsLive:()=>true},...dependencies});
}
test("administrator form exposes contact data, initial password and separate location and station grants",()=>{
  const {AdministratorsManager}=component("administrators-manager");
  const html=renderToStaticMarkup(React.createElement(AdministratorsManager,{initial:[],locations:[{id,name:"Depot",isArchived:false}],stations:[{id,name:"Point",code:"CP-001",isArchived:false}],language:"en"}));
  for(const text of ["Initial password","Assigned locations","Individually assigned stations","Depot","Point","Active account"])assert.ok(html.includes(text));
  assert.match(html,/type="password"[^>]*required/);assert.match(html,/minLength="12"/);assert.match(html,/type="checkbox"/);
});
test("locations are editable by Superadmin while the Admin view is read-only",()=>{
  const {LocationsManager}=component("locations-manager");
  const view=canEdit=>renderToStaticMarkup(React.createElement(LocationsManager,{initial:[],language:"en",canEdit}));
  assert.match(view(true),/Add location/);assert.doesNotMatch(view(false),/Add location/);assert.match(view(false),/<fieldset[^>]*disabled/);
});
test("operations dashboard shows all statuses and only Superadmin sees global account counts",()=>{
  const {OperationsOverview}=component("operations-overview",{"next/link":{default:({href,children,...props})=>React.createElement("a",{href,...props},children)}});
  const initial={stations:3,locations:1,administrators:2,drivers:4,statusCounts:{Available:1,Occupied:0,Charging:1,OutOfService:1,Maintenance:0,Unknown:0},activeSessions:1,completedSessions:2,energyKWh:4,calculatedCost:1.4,currency:"EUR"};
  const view=superadmin=>renderToStaticMarkup(React.createElement(OperationsOverview,{initial,language:"en",superadmin}));
  for(const label of ["Out of service","Maintenance","Unknown","Active sessions","Completed sessions","4.000 kWh","1.40 EUR"])assert.ok(view(true).includes(label));
  assert.match(view(true),/Active administrators/);assert.doesNotMatch(view(false),/Active administrators/);assert.doesNotMatch(view(false),/Active drivers/);
});
test("session filters convert local date fields to explicit UTC and preserve names and identifiers",()=>{
  const {sessionQuery}=component("sessions-monitor");const date="2026-10-04T13:30";
  const query=new URLSearchParams(sessionQuery({stationId:id,locationId:"",status:"Active",from:date,until:"",driver:"Arta Test",page:2}).slice(1));
  assert.equal(query.get("from"),new Date(date).toISOString());assert.equal(query.get("driver"),"Arta Test");assert.equal(query.get("page"),"2");
});
