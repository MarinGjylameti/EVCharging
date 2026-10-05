"use client";
import { useEffect, useState } from "react";
import type { Language } from "../../../../contracts/account";
import type { DeviceConnectionDetails, Device } from "../../../../contracts/operations";
import { failure, mutate, operations } from "./api";
import { useOperationsLive } from "./use-operations-live";
import { DeviceDriverForm } from "./device-driver-form";

export function DeviceManager({stationId,language,webSocketBaseUrl,disabled,canAssignDriver=false}:{stationId:string;language:Language;webSocketBaseUrl:string;disabled:boolean;canAssignDriver?:boolean}) {
  const sq=language==="sq";const [device,setDevice]=useState<Device|null>(null);const [loaded,setLoaded]=useState(false);
  const [busy,setBusy]=useState(false);const [error,setError]=useState("");
  const [identity,setIdentity]=useState("");const [serial,setSerial]=useState("");const [vendor,setVendor]=useState("");const [model,setModel]=useState("");const [tags,setTags]=useState("");
  const refresh=async(signal:AbortSignal)=>{const data=await operations<Device|null>("device",signal,`&id=${stationId}`);if(!signal.aborted){setDevice(data);setLoaded(true);}};
  const live=useOperationsLive(refresh);
  useEffect(()=>{const controller=new AbortController();void refresh(controller.signal).catch(e=>{if(!controller.signal.aborted)setError(failure(e,sq));});return()=>controller.abort();},[stationId,sq]);
  async function provision(){
    if(busy||disabled)return;setBusy(true);setError("");
    try{const result=await mutate<DeviceConnectionDetails>("device","provision",{identity,serialNumber:serial,vendor,model,protocol:"1.6J",authorizedTags:tags.split(/[\n,]/).map(x=>x.trim()).filter(Boolean)},stationId);setDevice(result.device);}
    catch(e){setError(failure(e,sq));}finally{setBusy(false);}
  }
  async function control(){
    if(!device||busy||disabled)return;
    if(!window.confirm(sq?"Të ndryshohet leja e lidhjes së karikuesit?":"Change the charger's connection permission?"))return;
    setBusy(true);setError("");
    try{setDevice(await mutate<Device>("device","control",{version:device.version,enabled:!device.isEnabled},stationId));}
    catch(e){setError(failure(e,sq));}finally{setBusy(false);}
  }
  const url=`${webSocketBaseUrl}/${device?.identity??identity}`;
  return <section className="operations-device"><h3>{sq?"Karikuesi fizik":"Physical charger"}</h3>{error&&<p className="form-error" role="alert">{error}</p>}
    {!loaded?<p>{sq?"Duke lexuar pajisjen…":"Loading device…"}</p>:device?<>
      <p><strong>{device.identity}</strong> · {device.isOnline?(sq?"I lidhur":"Connected"):(sq?"I shkëputur":"Disconnected")} · {live?(sq?"Përditësime aktive":"Live updates"):(sq?"Duke rilidhur përditësimet":"Reconnecting updates")}</p>
      <dl className="operations-details"><dt>{sq?"Seriali":"Serial"}</dt><dd>{device.serialNumber||"—"}</dd><dt>OCPP</dt><dd>{device.protocol}</dd><dt>{sq?"Lidhësit":"Connectors"}</dt><dd>{device.connectors.join(", ")}</dd><dt>{sq?"Mesazhi i fundit":"Last message"}</dt><dd>{device.lastSeenAt?new Date(device.lastSeenAt).toLocaleString(sq?"sq-AL":"en-GB"):"—"}</dd><dt>{sq?"Identifikues të autorizuar":"Authorized identifiers"}</dt><dd>{device.authorizedTagCount}</dd></dl>
      <label>{sq?"URL-ja e plotë WebSocket":"Full WebSocket URL"}<input readOnly value={url}/></label>
      <p>{sq?"Vendos vetëm këtë URL në karikues. Lidhja nuk kërkon username ose fjalëkalim.":"Configure only this URL on the charger. The connection does not require a username or password."}</p>
      <div className="operations-actions"><button type="button" className="button-outline" disabled={busy||disabled} onClick={()=>void control()}>{device.isEnabled?(sq?"Çaktivizo lidhjen":"Disable connection"):(sq?"Aktivizo lidhjen":"Enable connection")}</button></div>
      {canAssignDriver && <DeviceDriverForm device={device} stationId={stationId} sq={sq} disabled={disabled||busy} onSaved={setDevice}/>}
    </>:<form onSubmit={e=>{e.preventDefault();void provision();}}><fieldset disabled={busy||disabled} className="station-form-fields">
      <p>{sq?"Regjistro pajisjen, pastaj vendos vetëm URL-në në karikues. Nuk nevojitet username ose fjalëkalim. Zgjidh E lirë te forma e pikës për të hequr kufizimin manual.":"Register the device, then configure only its URL on the charger. No username or password is needed. Select Available in the station form to clear a manual restriction."}</p>
      <label>Charge Point ID<input required pattern="[A-Za-z0-9_-]{2,40}" maxLength={40} placeholder="CP-001" value={identity} onChange={e=>setIdentity(e.target.value)}/></label>
      <label>{sq?"Numri serial":"Serial number"}<input required maxLength={64} value={serial} onChange={e=>setSerial(e.target.value)}/></label>
      <div className="station-form-pair"><label>{sq?"Prodhuesi (opsional)":"Manufacturer (optional)"}<input maxLength={64} value={vendor} onChange={e=>setVendor(e.target.value)}/></label><label>{sq?"Modeli (opsional)":"Model (optional)"}<input maxLength={64} value={model} onChange={e=>setModel(e.target.value)}/></label></div>
      <label>{sq?"Protokolli":"Protocol"}<select value="1.6J" disabled><option value="1.6J">OCPP 1.6 JSON · ocpp1.6</option></select></label>
      <label>{sq?"Kartat e lejuara pa caktim Driver (opsionale)":"Allowed cards without Driver assignment (optional)"}<textarea maxLength={2100} rows={3} value={tags} onChange={e=>setTags(e.target.value)} placeholder="CARD-001"/></label>
      <small>{sq?"Një kod për rresht ose me presje. Pas regjistrimit, Superadmin mund të zgjedhë përdoruesin demo ose kodin/JSON e kartës te caktimi Driver. Kartat e kësaj liste mund të karikojnë edhe pa pronar Driver.":"One code per line or comma separated. After registration, Superadmin can choose the demo account or card code/JSON in Driver assignment. Cards in this list can charge even without an assigned Driver."}</small>
      <p>{sq?"Lidhësit regjistrohen 1 deri te numri i vendosur në pikë.":"Connectors are provisioned from 1 to the station's connector count."}</p>
      <button className="button-primary" disabled={busy}>{busy?(sq?"Duke regjistruar…":"Registering…"):(sq?"Regjistro karikuesin":"Register charger")}</button>
    </fieldset></form>}
  </section>;
}
