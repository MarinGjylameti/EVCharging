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
test("device provisioning returns the connection path through the authenticated same-origin BFF",async t=>{
  const route=bff(t)("operations");let calls=0;
  globalThis.fetch=async(url,init)=>{calls++;assert.equal(url,"https://api.example.test/api/v1/admin/stations/"+id+"/device");assert.equal(init.headers.get("Authorization"),"Bearer private-session");assert.equal(init.method,"POST");return Response.json({device:{identity:"CP-001"},webSocketPath:"/ws/cs/CP-001"});};
  const response=await route.POST(request({resource:"device",operation:"provision",id,input:{identity:"CP-001",serialNumber:"123",protocol:"1.6J",authorizedTags:["CARD"]}}));
  assert.equal(response.status,200);assert.equal(response.headers.get("cache-control"),"no-store");assert.deepEqual(await response.json(),{device:{identity:"CP-001"},webSocketPath:"/ws/cs/CP-001"});assert.equal(calls,1);
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
  return loadTypescript(new URL("../src/features/operations/"+name+".tsx",import.meta.url),{react:{...React,useEffect:()=>{},useState:initial=>[initial,()=>{}]},"./api":{},"./use-operations-live":{useOperationsLive:()=>true},"./device-driver-form":{DeviceDriverForm:()=>null},...dependencies});
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
test("physical charger panel provides only the URL and toggles access without credential rotation",async t=>{
  const previousWindow=globalThis.window;
  globalThis.window={confirm:()=>true};
  t.after(()=>{if(previousWindow===undefined)delete globalThis.window;else globalThis.window=previousWindow;});
  const device={id,identity:"CP-001",serialNumber:"123",protocol:"1.6J",connectors:[1,2],isEnabled:true,isOnline:false,authorizedTagCount:1,version:1};
  const state=[];let cursor=0,effect,pending;const calls=[];
  const {DeviceManager}=component("device-manager",{
    react:{...React,useState(initial){const index=cursor++;if(!(index in state))state[index]=initial;return [state[index],value=>{state[index]=value;}];},useEffect:fn=>{effect=fn;}},
    "./api":{operations:async()=>device,failure:()=>"Unexpected error",mutate:async(...args)=>{calls.push(args);pending=Promise.resolve({...device,isEnabled:args[2].enabled,version:device.version+1});return pending;}}
  });
  const render=()=>{cursor=0;return DeviceManager({stationId:id,language:"en",webSocketBaseUrl:"wss://api.example.test/ws/cs",disabled:false});};
  render();const cleanup=effect();t.after(()=>cleanup());await new Promise(resolve=>setImmediate(resolve));
  let tree=render();const html=renderToStaticMarkup(tree);
  assert.match(html,/wss:\/\/api.example.test\/ws\/cs\/CP-001/);
  assert.match(html,/does not require a username or password/);
  assert.doesNotMatch(html,/Rotate password|<label>Username|<label>Password|hide credentials/);
  function find(node,predicate){if(!node||typeof node!=="object")return undefined;if(predicate(node))return node;for(const child of React.Children.toArray(node.props?.children)){const found=find(child,predicate);if(found)return found;}}
  find(tree,node=>node.type==="button"&&node.props.children==="Disable connection").props.onClick();
  await pending;await new Promise(resolve=>setImmediate(resolve));
  assert.deepEqual(calls,[["device","control",{version:1,enabled:false},id]]);
  assert.match(renderToStaticMarkup(render()),/Enable connection/);
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

test("device Driver assignment BFF forwards the chosen account and card JSON to its protected endpoint",async t=>{
  const route=bff(t)("operations");const input={version:3,driverId:id,cardInput:'{"idTag":"CARD-001"}'};
  globalThis.fetch=async(url,init)=>{assert.equal(url,`https://api.example.test/api/v1/admin/stations/${id}/device/driver`);assert.equal(init.method,"PUT");assert.deepEqual(JSON.parse(init.body),input);return Response.json({assignedDriverId:id,cardIdTag:"CARD-001",version:4});};
  const response=await route.POST(request({resource:"device",operation:"assignDriver",id,input}));
  assert.equal(response.status,200);assert.equal((await response.json()).cardIdTag,"CARD-001");
});

test("device identity BFF updates the registered charger through its protected endpoint",async t=>{
  const route=bff(t)("operations");const input={version:3,identity:"NKR2023072805"};
  globalThis.fetch=async(url,init)=>{assert.equal(url,`https://api.example.test/api/v1/admin/stations/${id}/device/identity`);assert.equal(init.method,"PUT");assert.equal(init.headers.get("Authorization"),"Bearer private-session");assert.deepEqual(JSON.parse(init.body),input);return Response.json({identity:input.identity,version:4});};
  const response=await route.POST(request({resource:"device",operation:"updateIdentity",id,input}));
  assert.equal(response.status,200);assert.equal((await response.json()).identity,input.identity);
});

test("application log BFF forwards safe filters and retains Superadmin authorization failures",async t=>{
  const route=bff(t)("operations");let calls=0;
  globalThis.fetch=async(url,init)=>{calls++;assert.equal(url,"https://api.example.test/api/v1/admin/logs?source=OCPP&identity=NKR");assert.equal(init.headers.get("Authorization"),"Bearer private-session");return Response.json({code:"access_denied"},{status:403});};
  assert.equal((await route.GET(new Request("https://web.example.test/api/operations?resource=logs&source=OCPP&identity=NKR"))).status,403);
  for(const query of ["source=Database","identity="+"A".repeat(41),"identity=CP%0A001"])assert.equal((await route.GET(new Request("https://web.example.test/api/operations?resource=logs&"+query))).status,400);
  assert.equal(calls,1);
});

test("connection log view polls the backend, renders escaped messages and aborts when closed",async t=>{
  const previous={interval:globalThis.setInterval,clear:globalThis.clearInterval,document:globalThis.document};let poll,cleared=false;
  globalThis.setInterval=(fn,delay)=>{assert.equal(delay,5000);poll=fn;return 123;};globalThis.clearInterval=value=>{assert.equal(value,123);cleared=true;};globalThis.document={visibilityState:"visible"};
  t.after(()=>{globalThis.setInterval=previous.interval;globalThis.clearInterval=previous.clear;if(previous.document===undefined)delete globalThis.document;else globalThis.document=previous.document;});
  const state=[];let cursor=0,effect;const calls=[];
  const {ApplicationLogs}=component("application-logs",{
    react:{...React,useState(initial){const index=cursor++;if(!(index in state))state[index]=initial;return [state[index],value=>{state[index]=typeof value==="function"?value(state[index]):value;}];},useEffect:fn=>{effect=fn;}},
    "./api":{operations:async(...args)=>{calls.push(args);return {instanceId:"instance-123",startedAt:"2026-10-06T15:00:00Z",capacity:1000,entries:[{sequence:1,recordedAt:"2026-10-06T15:19:53Z",source:"OCPP",level:"Warning",identity:"NKR2023072805",message:"HTTP 401 <script>alert(1)</script>"}]};},failure:()=>"Cannot load logs"}
  });
  const render=()=>{cursor=0;return ApplicationLogs({language:"en"});};
  render();const cleanup=effect();await new Promise(resolve=>setImmediate(resolve));
  const html=renderToStaticMarkup(render());assert.match(html,/NKR2023072805/);assert.match(html,/HTTP 401 &lt;script&gt;/);assert.doesNotMatch(html,/<script>/);assert.match(html,/reset after a restart\/deploy/);
  assert.equal(calls[0][0],"logs");assert.equal(calls[0][2],"&source=OCPP");poll();await new Promise(resolve=>setImmediate(resolve));assert.equal(calls.length,2);
  cleanup();assert.equal(calls[0][1].aborted,true);assert.equal(cleared,true);
});

test("charger ID editor retains the saved URL until the backend accepts the new identity",async t=>{
  const device={id,identity:"CH-001",serialNumber:"123",protocol:"1.6J",connectors:[1],isEnabled:true,isOnline:false,authorizedTagCount:1,version:3};
  const state=[];let cursor=0,effect,complete,reject;const calls=[];
  const {DeviceManager}=component("device-manager",{
    react:{...React,useState(initial){const index=cursor++;if(!(index in state))state[index]=initial;return [state[index],value=>{state[index]=value;}];},useEffect:fn=>{effect=fn;}},
    "./api":{operations:async()=>device,failure:()=>"ID could not be saved",mutate:(...args)=>{calls.push(args);return new Promise((resolve,fail)=>{complete=resolve;reject=fail;});}}
  });
  const render=()=>{cursor=0;return DeviceManager({stationId:id,language:"en",webSocketBaseUrl:"wss://api.example.test/ws/cs",disabled:false});};
  function find(node,predicate){if(!node||typeof node!=="object")return undefined;if(predicate(node))return node;for(const child of React.Children.toArray(node.props?.children)){const found=find(child,predicate);if(found)return found;}}
  const url=()=>find(render(),node=>node.type==="input"&&node.props.readOnly).props.value;
  const save=()=>find(render(),node=>node.type==="button"&&node.props.children==="Save charger ID");
  render();const cleanup=effect();t.after(()=>cleanup());await new Promise(resolve=>setImmediate(resolve));
  assert.equal(save().props.disabled,true);
  find(render(),node=>node.type==="input"&&node.props.pattern).props.onChange({target:{value:"NKR2023072805"}});
  assert.equal(save().props.disabled,false);assert.equal(url(),"wss://api.example.test/ws/cs/CH-001");
  find(render(),node=>node.type==="form").props.onSubmit({preventDefault(){}});
  assert.equal(url(),"wss://api.example.test/ws/cs/CH-001");
  reject(new Error("Conflict"));await new Promise(resolve=>setImmediate(resolve));
  assert.equal(url(),"wss://api.example.test/ws/cs/CH-001");assert.match(renderToStaticMarkup(render()),/ID could not be saved/);
  find(render(),node=>node.type==="form").props.onSubmit({preventDefault(){}});
  complete({...device,identity:"NKR2023072805",version:4});await new Promise(resolve=>setImmediate(resolve));
  assert.equal(url(),"wss://api.example.test/ws/cs/NKR2023072805");assert.equal(save().props.disabled,true);
  assert.match(renderToStaticMarkup(render()),/ID saved and URL updated/);
  assert.deepEqual(calls,Array(2).fill(["device","updateIdentity",{version:3,identity:"NKR2023072805"},id]));
});

test("Driver assignment form explains empty demo mode, accepts JSON and submits the selected account",async t=>{
  const state=[];let cursor=0,effect;const saved=[],calls=[];
  const device={version:3,assignedDriverId:id,cardIdTag:null};
  const {DeviceDriverForm}=component("device-driver-form",{
    react:{...React,useState(initial){const index=cursor++;if(!(index in state))state[index]=initial;return [state[index],value=>{state[index]=value;}];},useEffect:fn=>{effect=fn;}},
    "./api":{operations:async()=>[{id,firstName:"Demo",lastName:"Driver",email:"demo@example.test",isActive:true}],failure:()=>"Invalid card input",mutate:async(...args)=>{calls.push(args);return {...device,version:4,cardIdTag:"CARD-001"};}}
  });
  const render=()=>{cursor=0;return DeviceDriverForm({device,stationId:id,sq:false,disabled:false,onSaved:value=>saved.push(value)});};
  function find(node,predicate){if(!node||typeof node!=="object")return undefined;if(predicate(node))return node;for(const child of React.Children.toArray(node.props?.children)){const found=find(child,predicate);if(found)return found;}}
  render();const cleanup=effect();t.after(()=>cleanup());await new Promise(resolve=>setImmediate(resolve));
  assert.match(renderToStaticMarkup(render()),/every new charge/);
  find(render(),node=>node.type==="textarea").props.onChange({target:{value:'{"idTag":"CARD-001"}'}});
  assert.match(renderToStaticMarkup(render()),/Only charges using this card/);
  render().props.onSubmit({preventDefault(){}});await new Promise(resolve=>setImmediate(resolve));
  assert.deepEqual(calls,[["device","assignDriver",{version:3,driverId:id,cardInput:'{"idTag":"CARD-001"}'},id]]);
  assert.equal(saved[0].cardIdTag,"CARD-001");assert.match(renderToStaticMarkup(render()),/Assignment saved/);
});
