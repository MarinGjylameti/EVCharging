import test from "node:test";
import assert from "node:assert/strict";
import { loadTypescript } from "./helpers/load-typescript.mjs";

function mount(t, measurements=false) {
  const names=["document","EventSource","setTimeout","clearTimeout"];
  const originals=Object.fromEntries(names.map(name=>[name,globalThis[name]]));
  const timers=new Map(),listeners={},requests=[],state=[],streams=[];
  let cleanup,nextTimer=1,refresh;
  const flush=async()=>{for(let i=0;i<12;i++)await Promise.resolve();};
  const react={
    useState:value=>{state.push(value);return [value,next=>state[0]=next];},
    useRef:value=>({current:value}),
    useEffect:fn=>{const result=fn();if(result)cleanup=result;}
  };
  globalThis.document={visibilityState:"visible",addEventListener:(name,fn)=>listeners[name]=fn,removeEventListener:name=>delete listeners[name]};
  globalThis.EventSource=class{
    handlers={};closed=false;
    constructor(url){assert.equal(url,"/api/operations/events");streams.push(this);}
    addEventListener(name,fn){this.handlers[name]=fn;}
    close(){this.closed=true;}
  };
  globalThis.setTimeout=fn=>{const id=nextTimer++;timers.set(id,fn);return id;};
  globalThis.clearTimeout=id=>timers.delete(id);
  refresh=signal=>new Promise((resolve,reject)=>requests.push({signal,resolve,reject}));
  const exports=loadTypescript(new URL("../src/features/operations/use-operations-live.ts",import.meta.url),{react});
  exports.useOperationsLive(refresh,measurements);
  t.after(async()=>{cleanup();await flush();for(const name of names){if(originals[name]===undefined)delete globalThis[name];else globalThis[name]=originals[name];}});
  const tick=async()=>{for(const [id,fn] of [...timers])if(timers.delete(id))fn();await flush();};
  return {streams,state,requests,listeners,timers,tick,flush,cleanup,
    event:(name,data)=>streams[0].handlers[name](data===undefined?undefined:{data:typeof data==="string"?data:JSON.stringify(data)}),
    resolve:async()=>{requests.at(-1).resolve();await flush();}};
}

test("metering, private command receipts and forecast changes do not reload analytics or unrelated resources",async t=>{
  const h=mount(t);
  h.event("ready");await h.tick();await h.resolve();
  for(let i=0;i<100;i++){
    h.event("changed",{eventType:"SessionMeasured",eventId:`meter-${i}`});
    h.event("changed",{eventType:"DeviceCommandChanged"});
    h.event("changed",{eventType:"StationChanged",telemetryOnly:true});
    h.event("heartbeat");
  }
  await h.tick();assert.equal(h.requests.length,1);assert.equal(h.state[0],true);
  for(const eventType of ["SessionStarted","SessionCompleted","SessionStatusChanged","StationChanged"]){
    h.event("changed",{eventType});await h.tick();await h.resolve();
  }
  assert.equal(h.requests.length,5);
});

test("the session monitor coalesces meter notices and refreshes after changes arriving during a snapshot",async t=>{
  const h=mount(t,true);
  for(let i=0;i<20;i++)h.event("changed",{eventType:"SessionMeasured"});
  await h.tick();assert.equal(h.requests.length,1);
  h.event("changed",{eventType:"SessionMeasured"});await h.tick();
  assert.equal(h.requests.length,1);
  await h.resolve();await h.tick();assert.equal(h.requests.length,2);await h.resolve();
});

test("SSE reconnection and returning to the page recover with an authorized snapshot; heartbeats retain the last view",async t=>{
  const h=mount(t);
  h.event("ready");await h.tick();await h.resolve();
  h.streams[0].onerror();assert.equal(h.state[0],false);
  await h.tick();await h.resolve();
  h.event("ready");await h.tick();await h.resolve();
  h.event("heartbeat");await h.tick();assert.equal(h.requests.length,3);
  document.visibilityState="hidden";h.listeners.visibilitychange();await h.tick();
  assert.equal(h.requests.length,3);
  document.visibilityState="visible";h.listeners.visibilitychange();await h.tick();await h.resolve();
  assert.equal(h.requests.length,4);assert.equal(h.state[0],true);
  for(const notice of ["invalid JSON",{},null]){h.event("changed",notice);await h.tick();await h.resolve();}
  assert.equal(h.requests.length,7);
});

test("snapshot failure reports a disconnected view and unmount cancels pending requests and timers",async t=>{
  const h=mount(t);
  h.event("ready");await h.tick();h.requests[0].reject(new Error("unavailable"));await h.flush();
  assert.equal(h.state[0],false);
  h.event("ready");await h.tick();
  h.event("changed",{eventType:"SessionCompleted"});
  h.cleanup();assert.equal(h.requests.at(-1).signal.aborted,true);
  assert.equal(h.streams[0].closed,true);assert.equal(h.timers.size,0);
  await h.resolve();await h.tick();assert.equal(h.requests.length,2);
});
