"use client";
import { useEffect, useRef, useState } from "react";

// Streams carry resource change notices. Fresh snapshots are authorized by the
// server, so revoked assignments cannot leave a private live delta on screen.
export function useOperationsLive(refresh:(signal:AbortSignal)=>Promise<void>) {
  const refreshRef=useRef(refresh); const [online,setOnline]=useState(false);
  useEffect(()=>{refreshRef.current=refresh;},[refresh]);
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
    const changed=()=>{setOnline(true);schedule();};
    events.addEventListener("ready",changed);events.addEventListener("changed",changed);events.addEventListener("heartbeat",changed);
    events.onerror=()=>{setOnline(false);schedule();};
    const visible=()=>{if(document.visibilityState==="visible") schedule();};
    document.addEventListener("visibilitychange",visible);
    return ()=>{controller.abort();events.close();clearTimeout(timer);document.removeEventListener("visibilitychange",visible);};
  },[]);
  return online;
}
