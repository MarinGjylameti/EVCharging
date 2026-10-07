"use client";
import { useEffect, useRef, useState } from "react";

// Streams carry resource change notices. Fresh snapshots are authorized by the
// server, so revoked assignments cannot leave a private live delta on screen.
export function useOperationsLive(refresh:(signal:AbortSignal)=>Promise<void>, measurements=false) {
  const refreshRef=useRef(refresh); const [online,setOnline]=useState(false);
  const measurementsRef=useRef(measurements);
  useEffect(()=>{refreshRef.current=refresh;measurementsRef.current=measurements;},[refresh,measurements]);
  useEffect(()=>{
    const controller=new AbortController(); let timer:ReturnType<typeof setTimeout>|undefined;
    let running=false; let pending=false;
    const run=async()=>{
      if(controller.signal.aborted) return;
      if(running) {pending=true;return;}
      running=true;
      try {await refreshRef.current(controller.signal);}
      catch {if(!controller.signal.aborted) setOnline(false);}
      finally {running=false;if(pending&&!controller.signal.aborted) {pending=false;schedule();}}
    };
    const schedule=()=>{if(!timer&&!controller.signal.aborted) timer=setTimeout(()=>{timer=undefined;void run();},100);};
    const events=new EventSource("/api/operations/events");
    const ready=()=>{setOnline(true);schedule();};
    const changed=(event:MessageEvent)=>{
      setOnline(true);
      try {
        const change=JSON.parse(event.data);
        // Forecast-only updates and private command receipts do not change these
        // resources. Only the session monitor needs snapshots for live metering.
        if(change?.telemetryOnly===true||change?.eventType==="DeviceCommandChanged")return;
        if(change?.eventType==="SessionMeasured"&&!measurementsRef.current)return;
      } catch { /* Unknown/legacy notices still recover through a snapshot. */ }
      schedule();
    };
    const heartbeat=()=>setOnline(true);
    events.addEventListener("ready",ready);events.addEventListener("changed",changed);events.addEventListener("heartbeat",heartbeat);
    events.onerror=()=>{setOnline(false);schedule();};
    const visible=()=>{if(document.visibilityState==="visible") schedule();};
    document.addEventListener("visibilitychange",visible);
    return ()=>{controller.abort();events.close();clearTimeout(timer);document.removeEventListener("visibilitychange",visible);};
  },[]);
  return online;
}
