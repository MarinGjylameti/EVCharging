"use client";
import { useState } from "react";
import type { Language,Notification } from "../../../../contracts/account";
import { operations } from "./api";
import { useOperationsLive } from "./use-operations-live";
export function OperationsNotifications({initial,language}:{initial:Notification[];language:Language}){
  const [items,setItems]=useState(initial);const sq=language==="sq";
  const live=useOperationsLive(async signal=>{const next=await operations<Notification[]>("notifications",signal);if(!signal.aborted)setItems(next);});
  return <><p role="status">{live?(sq?"Përditësime aktive":"Live updates"):(sq?"Duke lidhur përditësimet…":"Connecting live updates…")}</p>
    {items.length?<ul className="notification-list">{items.map(item=><li key={item.id}><div className="notification-mark" aria-hidden="true">↗</div><div><h2>{item.text.title}</h2><p>{item.text.body}</p><time dateTime={item.createdAt}>{new Date(item.createdAt).toLocaleString(sq?"sq-AL":"en-GB",{timeZone:"Europe/Tirane"})} · Tirana</time></div></li>)}</ul>:<p>{sq?"Ende asnjë njoftim.":"No notifications yet."}</p>}
  </>;
}
