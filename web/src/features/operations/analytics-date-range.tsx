"use client";
import { useState } from "react";
import { calendarDays, formatCalendar, moveMonth } from "./analytics-calendar";

export function AnalyticsDateRange({from,until,today,sq,disabled,onApply}:{from:string;until:string;today:string;sq:boolean;disabled:boolean;onApply:(from:string,until:string)=>void}) {
  const [open,setOpen]=useState(false);
  const [draft,setDraft]=useState({from,until});
  const [choosingEnd,setChoosingEnd]=useState(false);
  const [month,setMonth]=useState(from.slice(0,7)+"-01");
  const locale=sq?"sq-AL":"en-GB";
  const valid=!!draft.from&&!!draft.until&&draft.from<=draft.until&&draft.until<=today;
  const choose=(date:string)=>{
    if(!choosingEnd) {setDraft({from:date,until:date});setChoosingEnd(true);}
    else {setDraft(date<draft.from?{from:date,until:draft.from}:{from:draft.from,until:date});setChoosingEnd(false);}
  };
  return <div className="analytics-range">
    <button type="button" className="button-outline" disabled={disabled} aria-expanded={open} onClick={()=>{setDraft({from,until});setChoosingEnd(false);setMonth(from.slice(0,7)+"-01");setOpen(!open);}}>{sq?"Zgjidh intervalin":"Choose date range"}</button>
    {open&&<div className="analytics-range-popover">
      <div className="analytics-range-inputs">
        <label>{sq?"Nga data":"From date"}<input type="date" value={draft.from} max={today} required onChange={e=>{setDraft({...draft,from:e.target.value});if(e.target.value)setMonth(e.target.value.slice(0,7)+"-01");}}/></label>
        <label>{sq?"Deri më (përfshihet)":"Until date (inclusive)"}<input type="date" value={draft.until} min={draft.from} max={today} required onChange={e=>setDraft({...draft,until:e.target.value})}/></label>
      </div>
      <div className="analytics-calendar-navigation"><button type="button" aria-label={sq?"Muaji i mëparshëm":"Previous calendar month"} onClick={()=>setMonth(moveMonth(month,-1))} disabled={month==="0001-01-01"}>←</button><span>{choosingEnd?(sq?"Zgjidh datën e fundit":"Choose the end date"):(sq?"Zgjidh datën e parë":"Choose the start date")}</span><button type="button" aria-label={sq?"Muaji tjetër":"Next calendar month"} disabled={moveMonth(month,1)>today} onClick={()=>setMonth(moveMonth(month,1))}>→</button></div>
      <div className="analytics-calendars">{[month,moveMonth(month,1)].map(value=><div key={value} className="analytics-calendar">
        <h3>{formatCalendar(value,locale,true)}</h3>
        <div className="analytics-calendar-grid">{(sq?["Hë","Ma","Më","En","Pr","Sh","Di"]:["Mo","Tu","We","Th","Fr","Sa","Su"]).map(day=><span key={day} className="analytics-weekday">{day}</span>)}
          {calendarDays(value).map((date,i)=>date?<button type="button" key={date} disabled={date>today} aria-label={formatCalendar(date,locale)} aria-pressed={date>=draft.from&&date<=draft.until} className={`${date===draft.from||date===draft.until?"range-edge ":""}${date>=draft.from&&date<=draft.until?"in-range":""}`} onClick={()=>choose(date)}>{Number(date.slice(8))}</button>:<span key={`empty-${i}`}/>)}
        </div>
      </div>)}</div>
      {!valid&&<p role="status">{sq?"Zgjidh një interval të vlefshëm deri sot.":"Choose a valid range ending today or earlier."}</p>}
      <div className="analytics-range-actions"><button type="button" className="button-outline" onClick={()=>setOpen(false)}>{sq?"Anulo":"Cancel"}</button><button type="button" className="button-primary" disabled={!valid} onClick={()=>{onApply(draft.from,draft.until);setOpen(false);}}>{sq?"Zbato intervalin":"Apply range"}</button></div>
    </div>}
  </div>;
}
