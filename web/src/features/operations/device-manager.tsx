"use client";
import { useEffect, useState } from "react";
import type { Language } from "../../../../contracts/account";
import type { Credentials, Device } from "../../../../contracts/operations";
import { failure, mutate, operations } from "./api";
import { useOperationsLive } from "./use-operations-live";

export function DeviceManager({stationId,language,webSocketBaseUrl,disabled}:{stationId:string;language:Language;webSocketBaseUrl:string;disabled:boolean}) {
  const sq=language==="sq";const [device,setDevice]=useState<Device|null>(null);const [loaded,setLoaded]=useState(false);
  const [credentials,setCredentials]=useState<Credentials|null>(null);const [busy,setBusy]=useState(false);const [error,setError]=useState("");
  const [identity,setIdentity]=useState("");const [serial,setSerial]=useState("");const [vendor,setVendor]=useState("");const [model,setModel]=useState("");const [tags,setTags]=useState("");
  const refresh=async(signal:AbortSignal)=>{const data=await operations<Device|null>("device",signal,`&id=${stationId}`);if(!signal.aborted){setDevice(data);setLoaded(true);}};
  const live=useOperationsLive(refresh);
  useEffect(()=>{const controller=new AbortController();void refresh(controller.signal).catch(e=>{if(!controller.signal.aborted)setError(failure(e,sq));});return()=>controller.abort();},[stationId,sq]);
  async function provision(){
    if(busy||disabled)return;setBusy(true);setError("");setCredentials(null);
    try{const result=await mutate<Credentials>("device","provision",{identity,serialNumber:serial,vendor,model,protocol:"1.6J",authorizedTags:tags.split(/[\n,]/).map(x=>x.trim()).filter(Boolean)},stationId);setDevice(result.device);setCredentials(result);}
    catch(e){setError(failure(e,sq));}finally{setBusy(false);}
  }
  async function control(rotate:boolean){
    if(!device||busy||disabled)return;
    if(!window.confirm(rotate?(sq?"Të krijohet fjalëkalim i ri? Duhet përditësuar në karikues.":"Generate a new password? Update it on the charger."):(sq?"Të ndryshohet leja e lidhjes së karikuesit?":"Change the charger's connection permission?")))return;
    setBusy(true);setError("");setCredentials(null);
    try{const result=await mutate<Credentials|null>("device","control",{version:device.version,enabled:rotate?device.isEnabled:!device.isEnabled,rotatePassword:rotate},stationId);setCredentials(result);setDevice(await operations<Device|null>("device",undefined,`&id=${stationId}`));}
    catch(e){setError(failure(e,sq));}finally{setBusy(false);}
  }
  const url=`${webSocketBaseUrl}/${credentials?.device.identity??device?.identity??identity}`;
  return <section className="operations-device"><h3>{sq?"Karikuesi fizik":"Physical charger"}</h3>{error&&<p className="form-error" role="alert">{error}</p>}
    {!loaded?<p>{sq?"Duke lexuar pajisjen…":"Loading device…"}</p>:device?<>
      <p><strong>{device.identity}</strong> · {device.isOnline?(sq?"I lidhur":"Connected"):(sq?"I shkëputur":"Disconnected")} · {live?(sq?"Përditësime aktive":"Live updates"):(sq?"Duke rilidhur përditësimet":"Reconnecting updates")}</p>
      <dl className="operations-details"><dt>{sq?"Seriali":"Serial"}</dt><dd>{device.serialNumber||"—"}</dd><dt>OCPP</dt><dd>{device.protocol}</dd><dt>{sq?"Lidhësit":"Connectors"}</dt><dd>{device.connectors.join(", ")}</dd><dt>{sq?"Mesazhi i fundit":"Last message"}</dt><dd>{device.lastSeenAt?new Date(device.lastSeenAt).toLocaleString(sq?"sq-AL":"en-GB"):"—"}</dd><dt>{sq?"Identifikues të autorizuar":"Authorized identifiers"}</dt><dd>{device.authorizedTagCount}</dd></dl>
      <label>{sq?"URL-ja e plotë WebSocket":"Full WebSocket URL"}<input readOnly value={url}/></label>
      <div className="operations-actions"><button type="button" className="button-outline" disabled={busy||disabled} onClick={()=>void control(true)}>{sq?"Rinovo fjalëkalimin":"Rotate password"}</button><button type="button" className="button-outline" disabled={busy||disabled} onClick={()=>void control(false)}>{device.isEnabled?(sq?"Çaktivizo lidhjen":"Disable connection"):(sq?"Aktivizo lidhjen":"Enable connection")}</button></div>
    </>:<form onSubmit={e=>{e.preventDefault();void provision();}}><fieldset disabled={busy||disabled} className="station-form-fields">
      <p>{sq?"Regjistro pajisjen, pastaj vendos URL-në dhe kredencialet në karikues. Zgjidh E lirë te forma e pikës për të hequr kufizimin manual.":"Register the device, then configure its URL and credentials on the charger. Select Available in the station form to clear a manual restriction."}</p>
      <label>Charge Point ID<input required pattern="[A-Za-z0-9_-]{2,40}" maxLength={40} placeholder="CP-001" value={identity} onChange={e=>setIdentity(e.target.value)}/></label>
      <label>{sq?"Numri serial":"Serial number"}<input required maxLength={64} value={serial} onChange={e=>setSerial(e.target.value)}/></label>
      <div className="station-form-pair"><label>{sq?"Prodhuesi (opsional)":"Manufacturer (optional)"}<input maxLength={64} value={vendor} onChange={e=>setVendor(e.target.value)}/></label><label>{sq?"Modeli (opsional)":"Model (optional)"}<input maxLength={64} value={model} onChange={e=>setModel(e.target.value)}/></label></div>
      <label>{sq?"Protokolli":"Protocol"}<select value="1.6J" disabled><option value="1.6J">OCPP 1.6 JSON · ocpp1.6</option></select></label>
      <label>{sq?"Identifikuesit e autorizuar (idTag)":"Authorized identifiers (idTag)"}<textarea required maxLength={2100} rows={3} value={tags} onChange={e=>setTags(e.target.value)} placeholder="CARD-001"/></label>
      <small>{sq?"Një identifikues për rresht ose me presje, siç e dërgon karikuesi. Vetëm këto idTag pranohen për Authorize/StartTransaction; kërkoji kolegut vlerat që përdor pajisja.":"One identifier per line or comma separated, exactly as sent by the charger. Only these idTags are accepted for Authorize/StartTransaction; obtain the device's values from your colleague."}</small>
      <p>{sq?"Lidhësit regjistrohen 1 deri te numri i vendosur në pikë.":"Connectors are provisioned from 1 to the station's connector count."}</p>
      <button className="button-primary" disabled={busy}>{busy?(sq?"Duke regjistruar…":"Registering…"):(sq?"Regjistro karikuesin":"Register charger")}</button>
    </fieldset></form>}
    {credentials&&<div className="operations-credentials" role="status"><h4>{sq?"Kopjo për konfigurimin e karikuesit":"Copy for charger configuration"}</h4>
      <label>WebSocket<input readOnly value={url}/></label><label>Username<input readOnly value={credentials.username}/></label><label>Password<input readOnly autoComplete="off" value={credentials.password}/></label>
      <p>{sq?"Fjalëkalimi i pajisjes për serverin, i veçantë nga llogaria Admin. Shfaqet vetëm tani; pas mbylljes mund ta rinovosh.":"The device's server password, separate from the Admin account. It is shown now; rotate it if you need a new one later."}</p>
      <button type="button" className="button-outline" onClick={()=>setCredentials(null)}>{sq?"I ruajta, fsheh kredencialet":"Saved, hide credentials"}</button>
    </div>}
  </section>;
}
