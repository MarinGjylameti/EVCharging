"use client";

import { useRef, useState } from "react";
import type { Language, Station } from "../../../../contracts/account";
import { StationMapPicker } from "./station-map-picker";
import type { Location } from "../../../../contracts/operations";
import { DeviceManager } from "../operations/device-manager";
import { useOperationsLive } from "../operations/use-operations-live";
import { operations } from "../operations/api";

type Draft = Pick<Station, "code"|"name"|"city"|"address"|"latitude"|"longitude"|"connectorType"|"connectorCount"|"powerKW"|"pricePerKWh"|"status"|"version"> & {locationId:string|null;reason:string};
const blank: Draft = {code:"",name:"",city:"",address:"",latitude:41.3275,longitude:19.8187,
  connectorType:"CCS2",connectorCount:1,powerKW:50,pricePerKWh:0.35,status:"Available",version:0,locationId:null,reason:""};
const statuses: Station["status"][] = ["Available","Occupied","Charging","OutOfService","Maintenance","Unknown"];
const labels: Record<Station["status"],{sq:string;en:string}> = {
  Available:{sq:"E lirë",en:"Available"}, Occupied:{sq:"E zënë",en:"Occupied"},
  Charging:{sq:"Në karikim",en:"Charging"}, OutOfService:{sq:"Jashtë funksionit",en:"Out of service"},
  Maintenance:{sq:"Në mirëmbajtje",en:"Maintenance"}, Unknown:{sq:"Pa informacion aktual",en:"No current information"}
};
const formOf = (station: Station): Draft => ({code:station.code,name:station.name,city:station.city,
  address:station.address,latitude:station.latitude,longitude:station.longitude,connectorType:station.connectorType,
  connectorCount:station.connectorCount,powerKW:station.powerKW,pricePerKWh:station.pricePerKWh,
  status:station.status,version:station.version,locationId:station.locationId??null,reason:""});

export function StationManager({initial, language, canEdit, locations:initialLocations=[], superadmin=false, webSocketBaseUrl=""}: {initial:Station[];language:Language;canEdit:boolean;locations?:Location[];superadmin?:boolean;webSocketBaseUrl?:string}) {
  const sq = language === "sq";
  const [stations,setStations] = useState(initial);
  const [locations,setLocations] = useState(initialLocations);
  const live=useOperationsLive(async signal=>{const [points,places]=await Promise.all([operations<Station[]>("stations",signal),operations<Location[]>("locations",signal)]);if(!signal.aborted){setStations(points);setLocations(places);}});
  const [selectedId,setSelectedId] = useState<string|null>(initial[0]?.id ?? null);
  const [draft,setDraft] = useState<Draft>(initial[0] ? formOf(initial[0]) : blank);
  const [busy,setBusy] = useState(false);
  const [error,setError] = useState("");
  const [message,setMessage] = useState("");
  const [filter,setFilter] = useState("");
  const [mapOpen,setMapOpen] = useState(false);
  const [locationChosen,setLocationChosen] = useState(Boolean(initial[0]));
  const mapButtonRef = useRef<HTMLButtonElement>(null);
  const selected = stations.find(x=>x.id===selectedId);
  const shown = stations.filter(x=>(x.name+" "+x.code+" "+x.city).toLocaleLowerCase().includes(filter.toLocaleLowerCase()));
  const active = stations.filter(x=>!x.isArchived).length;
  const available = stations.filter(x=>!x.isArchived&&x.status==="Available").length;
  const setField = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft(previous=>({...previous,[key]:value}));
  function select(station: Station) { setSelectedId(station.id); setDraft(formOf(station)); setLocationChosen(true); setMapOpen(false); setError(""); setMessage(""); }
  function create() { setSelectedId(null); setDraft({...blank,locationId:locations.find(x=>!x.isArchived)?.id??null}); setLocationChosen(false); setMapOpen(false); setError(""); setMessage(""); }
  function closeMap() { setMapOpen(false); requestAnimationFrame(()=>mapButtonRef.current?.focus()); }
  async function send(operation:"create"|"update"|"archive") {
    if (busy || !canEdit) return;
    if (operation==="create" && !locationChosen) return;
    if (operation==="archive" && !window.confirm(sq?"Ta arkivojmë këtë pikë?":"Archive this station?")) return;
    setBusy(true); setError(""); setMessage("");
    try {
      const response = await fetch("/api/stations", {method:"POST",headers:{"Content-Type":"application/json"},
        body:JSON.stringify(operation==="create" ? {operation,station:draft} : operation==="update" ?
          {operation,id:selectedId,station:draft} : {operation,id:selectedId,version:selected?.version})});
      if (response.status===401) { window.location.assign("/"); return; }
      const data = await response.json();
      if (!response.ok) {
        setError(response.status===412 ? (sq?"Pika ka ndryshuar. Rifresko faqen.":"The station changed. Refresh the page.") :
          data.code==="station_code_taken" ? (sq?"Ky kod përdoret tashmë.":"This code already exists.") :
          data.code==="station_busy" ? (sq?"Përfundo karikimin aktiv përpara këtij veprimi.":"Finish the active charge before this action.") :
          data.code==="device_connectors_locked" ? (sq?"Mund të shtosh lidhës te një pajisje fizike; lidhësit ekzistues nuk mund të hiqen.":"You can add connectors to a physical device; existing connectors cannot be removed.") :
          sq?"Kontrollo të dhënat dhe provo përsëri.":"Check the details and try again.");
        return;
      }
      const station = data as Station;
      setStations(previous=>previous.some(x=>x.id===station.id) ? previous.map(x=>x.id===station.id?station:x) : [...previous,station]);
      setSelectedId(station.id); setDraft(formOf(station)); setLocationChosen(true);
      setMessage(operation==="archive" ? (sq?"Pika u arkivua.":"Station archived.") :
        (sq?"Pika u ruajt.":"Station saved."));
    } catch { setError(sq?"Lidhja dështoi. Provo përsëri.":"Connection failed. Please retry."); }
    finally { setBusy(false); }
  }
  return <div className="workspace-body station-workspace">
    <span className="eyebrow">{sq?"RRJETI I KARIKIMIT":"CHARGING NETWORK"}</span>
    <div className="station-heading"><div><h1>{sq?"Pikat e karikimit":"Charging stations"}</h1>
      <p className="lead">{sq?"Koordinatat, lidhësit, çmimet dhe gjendja e pikave.":"Coordinates, connectors, prices and station status."}</p></div>
      {canEdit&&<button type="button" className="button-outline" onClick={create}>＋ {sq?"Shto pikë":"Add station"}</button>}</div>
    <div className="station-summary"><article><span>{sq?"PIKA AKTIVE":"ACTIVE STATIONS"}</span><strong>{active}</strong></article>
      <article><span>{sq?"TË LIRA":"AVAILABLE"}</span><strong>{available}</strong></article>
      <article><span>{sq?"QYTETE":"CITIES"}</span><strong>{new Set(stations.filter(x=>!x.isArchived).map(x=>x.city)).size}</strong></article></div>
    <p className="station-disclaimer" role="status">{live?(sq?"Përditësime aktive nga karikuesit.":"Live charger updates."):(sq?"Duke lidhur përditësimet…":"Connecting live updates…")}</p>
    <div className="station-admin-grid"><section className="station-table-card">
      <div className="station-table-top"><h2>{sq?"Lista e pikave":"Station list"}</h2><input aria-label={sq?"Kërko pikë":"Search stations"}
        placeholder={sq?"Kërko sipas emrit, kodit, qytetit":"Search name, code or city"} value={filter} onChange={e=>setFilter(e.target.value)}/></div>
      {shown.length ? shown.map(station=><button type="button" key={station.id} className="station-admin-row"
        aria-pressed={station.id===selectedId} onClick={()=>select(station)}>
        <span className={`station-dot status-${station.status.toLowerCase()}`}/>
        <span className="station-admin-name"><strong>{station.name}</strong><small>{station.city} · {station.code}{station.isDemo?" · Demo":""}</small></span>
        <span className="station-admin-meta">{station.powerKW} kW<small>{station.isArchived?(sq?"Arkivuar":"Archived"):labels[station.status][language]}</small></span>
      </button>) : <p className="station-empty">{sq?"Nuk u gjet asnjë pikë.":"No stations found."}</p>}
    </section><section className="station-edit-card">
      <div className="station-form-head"><span className="eyebrow">{selectedId?(sq?"DETAJET E PIKËS":"STATION DETAILS"):(sq?"PIKË E RE":"NEW STATION")}</span>
        <h2>{selected?.name ?? (sq?"Shto pikë karikimi":"Add charging station")}</h2></div>
      {error&&<p role="alert" className="form-error">{error}</p>}{message&&<p role="status" className="form-success">{message}</p>}
      {(canEdit || selected) ? <form onSubmit={event=>{event.preventDefault();void send(selectedId?"update":"create");}}>
        <fieldset disabled={busy||!canEdit||selected?.isArchived} className="station-form-fields">
          <label>{sq?"Vendndodhja":"Location"}<select required={!superadmin&&!selectedId} value={draft.locationId??""} onChange={e=>setField("locationId",e.target.value||null)}><option value="">{sq?"Zgjidh vendndodhjen":"Choose location"}</option>{draft.locationId&&!locations.some(x=>x.id===draft.locationId)&&<option value={draft.locationId}>{sq?"Vendndodhja ekzistuese":"Current location"}</option>}{locations.filter(x=>!x.isArchived||x.id===draft.locationId).map(x=><option key={x.id} value={x.id}>{x.name} · {x.city}</option>)}</select></label>
          <label>{sq?"Kodi":"Code"}<input required minLength={2} maxLength={40} value={draft.code} onChange={e=>setField("code",e.target.value)}/></label>
          <label>{sq?"Emri":"Name"}<input required minLength={2} maxLength={120} value={draft.name} onChange={e=>setField("name",e.target.value)}/></label>
          <div className="station-form-pair"><label>{sq?"Qyteti":"City"}<input required value={draft.city} onChange={e=>setField("city",e.target.value)}/></label>
            <label>{sq?"Lidhësi":"Connector"}<input required value={draft.connectorType} onChange={e=>setField("connectorType",e.target.value)}/></label></div>
          <label>{sq?"Adresa":"Address"}<input required value={draft.address} onChange={e=>setField("address",e.target.value)}/></label>
          {canEdit&&<div className="station-location-choice"><button type="button" className="button-outline" ref={mapButtonRef}
            onClick={()=>setMapOpen(true)}>⌖ {sq?"Zgjidh pikën në hartë":"Choose point on map"}</button>
            <small>{sq?"Kliko vendin e saktë në hartë; adresa shkruhet veçmas.":"Click the exact spot on the map; enter the address separately."}</small></div>}
          <div className="station-form-pair"><label>Latitude<input required type="number" min="-90" max="90" step="any" readOnly={!selectedId} value={locationChosen?draft.latitude:""} onChange={e=>setField("latitude",Number(e.target.value))}/></label>
            <label>Longitude<input required type="number" min="-180" max="180" step="any" readOnly={!selectedId} value={locationChosen?draft.longitude:""} onChange={e=>setField("longitude",Number(e.target.value))}/></label></div>
          <div className="station-form-pair"><label>{sq?"Numri i lidhësve":"Connector count"}<input required type="number" min="1" max="100" value={draft.connectorCount} onChange={e=>setField("connectorCount",Number(e.target.value))}/></label>
            <label>{sq?"Fuqia (kW)":"Power (kW)"}<input required type="number" min="0.01" step="0.01" value={draft.powerKW} onChange={e=>setField("powerKW",Number(e.target.value))}/></label></div>
          <div className="station-form-pair"><label>{sq?"Çmimi (EUR/kWh)":"Price (EUR/kWh)"}<input required type="number" min="0" step="0.0001" value={draft.pricePerKWh} onChange={e=>setField("pricePerKWh",Number(e.target.value))}/></label>
            <label>{sq?"Gjendja":"Status"}<select value={draft.status} onChange={e=>setField("status",e.target.value as Station["status"])}>
              {statuses.map(status=><option value={status} key={status}>{labels[status][language]}</option>)}</select></label></div>
          <label>{sq?"Arsyeja e ndryshimit të gjendjes/çmimit":"Reason for status/price change"}<textarea required={!!selected&&(draft.status!==selected.status||draft.pricePerKWh!==selected.pricePerKWh)} minLength={3} maxLength={500} value={draft.reason} onChange={e=>setField("reason",e.target.value)}/></label>
          {selected&&selected.version!==draft.version&&<p className="form-error">{sq?"Pika ka ndryshuar. Klikoje sërish në listë për të marrë versionin e ri para ruajtjes.":"The station changed. Select it again in the list before saving."}</p>}
          {canEdit&&!selected?.isArchived&&<button className="button-primary" type="submit" disabled={!selectedId&&!locationChosen}>{busy?(sq?"Duke ruajtur…":"Saving…"):(sq?"Ruaj pikën":"Save station")}</button>}
        </fieldset>
      </form> : <p className="lead">{sq?"Zgjidh një pikë nga lista.":"Choose a station from the list."}</p>}
      {canEdit&&selected&&!selected.isArchived&&<button type="button" className="station-archive" disabled={busy||["Charging","Occupied"].includes(selected.status)} onClick={()=>void send("archive")}>
        {sq?"Arkivo pikën":"Archive station"}</button>}
      {!canEdit&&<p className="station-readonly">{sq?"Vetëm Superadmin mund të ndryshojë pikat.":"Only Superadmin can change stations."}</p>}
      {selected&&!selected.isDemo&&<DeviceManager key={selected.id} stationId={selected.id} language={language} webSocketBaseUrl={webSocketBaseUrl} disabled={!canEdit||!!selected.isArchived} canAssignDriver={superadmin}/>}
    </section></div>
    {mapOpen&&<StationMapPicker initial={{latitude:draft.latitude,longitude:draft.longitude}} language={language}
      requireClick={!selectedId} onClose={closeMap} onConfirm={point=>{
        setDraft(previous=>({...previous,latitude:point.latitude,longitude:point.longitude}));
        setLocationChosen(true);
        closeMap(); setMessage("");
      }}/>}
  </div>;
}
