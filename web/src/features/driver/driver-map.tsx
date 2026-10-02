"use client";

import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useDriverLiveData } from "./use-driver-live-data";
import type { ChargingHistory, Language, Station } from "../../../../contracts/account";
import { albanianCities } from "@/lib/albanian-cities";
import { remainingChargingMinutes } from "@/lib/availability";
import { directionsUrl, mapProviderFromPreference, mapProviderStorageKey,
  type MapProvider } from "@/lib/map-directions";
import { distanceKm, fromWorldPixels, pinchMapView, toWorldPixels, visibleTiles, zoomAroundPixel, type Coordinates, type MapView, type WorldPoint } from "@/lib/map-geometry";

const statusSq: Record<Station["status"], string> = {
  Available:"E lirë", Occupied:"E zënë", Charging:"Në karikim",
  OutOfService:"Jashtë funksionit", Maintenance:"Në mirëmbajtje", Unknown:"Pa informacion aktual"
};
const statusEn: Record<Station["status"], string> = {
  Available:"Available", Occupied:"Occupied", Charging:"Charging",
  OutOfService:"Out of service", Maintenance:"Maintenance", Unknown:"No current information"
};
const euro = (value: number) => new Intl.NumberFormat("sq-AL", {minimumFractionDigits:2, maximumFractionDigits:2}).format(value);
const centerOf = (stations: Station[]): Coordinates => stations.length ? {
  latitude:stations.reduce((sum,x)=>sum+x.latitude,0)/stations.length,
  longitude:stations.reduce((sum,x)=>sum+x.longitude,0)/stations.length
} : {latitude:41.3275,longitude:19.8187};

type Props = {stations: Station[]; history: ChargingHistory[]; language: Language; firstName: string};
function AvailabilityCard({station,language,nowMs}: {
  station: Station; language: Language; nowMs: number|null;
}) {
  if (station.status!=="Charging") return null;
  const sq=language==="sq";
  const forecast=station.availabilityForecast;
  const remaining=nowMs===null ? null : remainingChargingMinutes(forecast,nowMs);
  return <div className="station-forecast">
    <span className="driver-eyebrow">{forecast?.source==="Demo" ?
      (sq?"SHEMBULL DEMO":"DEMO EXAMPLE") : (sq?"KOHA E MBETUR":"TIME REMAINING")}</span>
    {remaining!==null && forecast ? <>
      <strong>{sq?`Rreth ${remaining} min të mbetura`:`About ${remaining} min remaining`}</strong>
      <small>{sq?`Lidhësi ${forecast.connectorId} · vlerësim nga ${forecast.source==="Demo"?"simulimi":"pajisja"}`:
        `Connector ${forecast.connectorId} · ${forecast.source==="Demo"?"simulated":"device"} estimate`}</small>
      <small>{sq?"Vlerësimi mund të ndryshojë dhe nuk rezervon lidhësin.":
        "The estimate can change and does not reserve the connector."}</small>
    </> : <p>{nowMs===null ? (sq?"Po kontrolloj kohën e mbetur…":"Checking remaining time…") : forecast ?
      (sq?"Vlerësimi i fundit ka skaduar. Duhet një përditësim i ri nga burimi i të dhënave.":
        "The latest estimate expired. A new update from the data source is needed.") :
      (sq?"Nuk ka ende një vlerësim të kohës së mbetur për këtë karikim.":
        "No remaining-time estimate is available for this charge yet.")}</p>}
  </div>;
}

export function DriverMap({stations: initialStations, history: initialHistory, language, firstName}: Props) {
  const sq = language === "sq";
  const router = useRouter();
  const { stations, history, connected, unavailable } = useDriverLiveData(initialStations, initialHistory);
  const stationCities = useMemo(() => [...new Set(stations.map(x => x.city))].sort((a,b)=>a.localeCompare(b,"sq")), [stations]);
  const cityOptions = useMemo(() => [...new Set([...albanianCities,...stationCities])].sort((a,b)=>a.localeCompare(b,"sq")), [stationCities]);
  const [city, setCity] = useState<string|null>(null);
  const [filter, setFilter] = useState<"all" | "available">("all");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [view, setView] = useState({center:centerOf(stations),zoom:10});
  const {center,zoom} = view;
  const [driverLocation, setDriverLocation] = useState<Coordinates|null>(null);
  const [locationStatus, setLocationStatus] = useState<"locating"|"ready"|"unavailable">("locating");
  const [size, setSize] = useState({width:0,height:0});
  const [nowMs, setNowMs] = useState<number|null>(null);
  const [mapProvider, setMapProvider] = useState<MapProvider>("google");
  const mapRef = useRef<HTMLDivElement>(null);
  const stationPanelRef = useRef<HTMLDivElement>(null);
  const stationsRef = useRef(stations);
  const dragging = useRef<{id:number,x:number,y:number,worldX:number,worldY:number}|null>(null);
  const pointers = useRef(new Map<number,{x:number;y:number}>());
  const pinch = useRef<{view:MapView;midpoint:WorldPoint;distance:number}|null>(null);
  const wheelDelta = useRef(0);
  const mounted = useRef(false);
  const locationRequest = useRef(0);
  useEffect(() => { stationsRef.current=stations; }, [stations]);
  useEffect(() => {
    const readPreference = () => {
      try { setMapProvider(mapProviderFromPreference(localStorage.getItem(mapProviderStorageKey))); }
      catch { setMapProvider("google"); }
    };
    readPreference();
    window.addEventListener("storage", readPreference);
    return () => window.removeEventListener("storage", readPreference);
  }, []);
  useEffect(() => {
    setNowMs(Date.now());
    const clock=setInterval(()=>setNowMs(Date.now()),30_000);
    return () => {clearInterval(clock);};
  }, []);
  const locate = useCallback(() => {
    if (!navigator.geolocation) { setLocationStatus("unavailable"); return; }
    const request = ++locationRequest.current;
    setLocationStatus("locating");
    navigator.geolocation.getCurrentPosition(position => {
      if (!mounted.current || request!==locationRequest.current) return;
      const point = {latitude:position.coords.latitude,longitude:position.coords.longitude};
      const closest = [...stationsRef.current].sort((a,b)=>distanceKm(point,a)-distanceKm(point,b))[0];
      setDriverLocation(point); setLocationStatus("ready"); setCity(null);
      setView({center:point,zoom:13}); setSelectedId(closest?.id ?? null);
    }, () => { if (mounted.current && request===locationRequest.current) setLocationStatus("unavailable"); },
    {enableHighAccuracy:true,timeout:10000,maximumAge:60000});
  }, []);
  useEffect(() => { mounted.current=true; locate(); return () => { mounted.current=false; locationRequest.current++; }; }, [locate]);
  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const observer = new ResizeObserver(() => setSize({width:map.clientWidth,height:map.clientHeight}));
    observer.observe(map);
    setSize({width:map.clientWidth,height:map.clientHeight});
    return () => observer.disconnect();
  }, []);
  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const delta = event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? map.clientHeight : 1);
      wheelDelta.current += delta;
      if (Math.abs(wheelDelta.current)<60) return;
      const direction = wheelDelta.current>0 ? -1 : 1;
      wheelDelta.current=0;
      const rect = map.getBoundingClientRect();
      const x=event.clientX-rect.left,y=event.clientY-rect.top;
      setView(previous=>{
        const nextZoom=Math.max(6,Math.min(19,previous.zoom+direction));
        if (nextZoom===previous.zoom) return previous;
        return {zoom:nextZoom,center:zoomAroundPixel(previous.center,previous.zoom,nextZoom,
          rect.width,rect.height,x,y)};
      });
    };
    map.addEventListener("wheel",onWheel,{passive:false});
    return () => map.removeEventListener("wheel",onWheel);
  }, []);
  const inMyArea = city===null && driverLocation!==null;
  const candidateStations = useMemo(() => inMyArea ? [...stations].sort((a,b)=>
    distanceKm(driverLocation!,a)-distanceKm(driverLocation!,b)) : city ? stations.filter(x=>x.city===city) : stations,
    [stations,city,driverLocation,inMyArea]);
  const shown = candidateStations.filter(x => filter === "all" || x.status === "Available");
  const active = shown.find(x => x.id === selectedId) ?? shown[0] ?? null;
  useEffect(() => { if (stationPanelRef.current) stationPanelRef.current.scrollTop = 0; }, [active?.id]);
  const selectedCityHasStations = city ? stations.some(x=>x.city===city) : false;
  const tiles = visibleTiles(center,zoom,size.width,size.height);
  const worldCenter = toWorldPixels(center,zoom);
  const nearbyCount = driverLocation ? stations.filter(x=>distanceKm(driverLocation,x)<=10).length : 0;
  const available = stations.filter(x=>x.status==="Available").length;
  const totalEnergy = history.reduce((sum,x)=>sum+x.energyKWh,0);
  const sessionCount = history.length;
  function position(station: Coordinates) {
    const point = toWorldPixels(station,zoom);
    return {left:point.x-worldCenter.x+size.width/2,top:point.y-worldCenter.y+size.height/2};
  }
  return <div className="driver-content">
    {stations.some(x=>x.isDemo) && <div className="demo-banner" role="note"><span aria-hidden="true">✦</span>
      {sq ? "Pamje demo: pikat, gjendjet dhe historiku janë shembuj për testim." : "Demo preview: stations, status and history are sample data."}</div>}
    <div className="driver-welcome"><div><span className="driver-eyebrow">{sq ? "RRUGA JOTE FILLON KËTU" : "YOUR JOURNEY STARTS HERE"}</span>
      <h1>{sq ? `Përshëndetje, ${firstName}.` : `Hello, ${firstName}.`}</h1>
      <p>{sq ? "Gjej pikën e përshtatshme dhe shiko karikimet e tua." : "Find the right charge point and see your charging history."}</p></div>
      <Link href="/driver/history" className="driver-history-link">{sq ? "Shiko historikun" : "View history"} <span>↗</span></Link></div>
    <div className="driver-kpis">
      <article><span>{sq ? "PIKA NË HARTË" : "POINTS ON MAP"}</span><strong>{stations.length}</strong><small>{stationCities.length} {sq?"qytete":"cities"}</small></article>
      <article><span>{sq ? "TË SHËNUARA TË LIRA" : "MARKED AVAILABLE"}</span><strong className="green">{available}</strong><small>{sq ? "sipas gjendjes së ruajtur" : "from saved status"}</small></article>
      <article><span>{sq ? "KARIKIMET E MIA" : "MY CHARGES"}</span><strong>{sessionCount}</strong><small>{totalEnergy.toFixed(1)} kWh {sq ? "gjithsej" : "total"}</small></article>
    </div>
    <section className="driver-map-section" aria-labelledby="map-title">
      <div className="driver-section-heading"><div><span className="driver-eyebrow">{sq ? "EKSPLORO RRJETIN" : "EXPLORE THE NETWORK"}</span>
        <h2 id="map-title">{sq ? "Pikat e karikimit" : "Charging stations"}</h2></div>
        <span className="driver-map-counter">{inMyArea ?
          (sq?`${nearbyCount} pika brenda 10 km`:`${nearbyCount} stations within 10 km`) :
          (sq?`${shown.length} pika në listë`:`${shown.length} stations listed`)}</span></div>
      <div className="driver-toolbar">
        <div className="driver-city-controls">
          <label className="driver-city-select"><span>{sq?"Qyteti / bashkia":"City / municipality"}</span>
            <select value={city??""} onChange={event=>{
              const value=event.target.value;
              setCity(value||null); setSelectedId(null);
              if (value) {
                const cityStations=stations.filter(x=>x.city===value);
                if (cityStations.length) setView({center:centerOf(cityStations),zoom:14});
              } else setView({center:driverLocation??centerOf(stations),zoom:driverLocation?13:10});
            }}>
              <option value="">{sq?"Zgjidh qytetin (opsionale)":"Choose a city (optional)"}</option>
              {cityOptions.map(value=><option value={value} key={value}>{value}</option>)}
            </select>
          </label>
          <button type="button" className="driver-locate-button" onClick={()=>{
            if (!driverLocation) { locate(); return; }
            setCity(null); setView({center:driverLocation,zoom:13}); setSelectedId(null);
          }}>{sq?"⌖ Vendndodhja ime":"⌖ My location"}</button>
        </div>
        <div className="map-filter" role="group" aria-label={sq ? "Filtri i pikave" : "Station filter"}>
          <button type="button" aria-pressed={filter==="all"} onClick={()=>setFilter("all")}>{sq ? "Të gjitha" : "All"}</button>
          <button type="button" aria-pressed={filter==="available"} onClick={()=>setFilter("available")}>{sq ? "Vetëm të lira" : "Available"}</button>
        </div>
      </div>
      <p className="driver-location-status" role="status">{locationStatus==="locating" ?
        (sq?"Po kërkoj vendndodhjen tënde…":"Finding your location…") : locationStatus==="unavailable" ?
        (sq?"Vendndodhja nuk u lexua. Lejo aksesin në shfletues dhe provo ‘Vendndodhja ime’.":"Location is unavailable. Allow browser access and try ‘My location’ again.") :
        (sq?`Vendndodhja u gjet. ${nearbyCount} pika brenda 10 km.`:`Location found. ${nearbyCount} stations within 10 km.`)}</p>
      <p className="map-touch-help">{sq ? "Lëvize hartën me gisht; zmadhoje me dy gishta." : "Drag the map with one finger; pinch with two to zoom."}</p>
      <div className="map-layout">
        <div className="map-stage" ref={mapRef} role="application" aria-label={sq ? `Harta e pikave ${city??"pranë meje"}` : `Charging map ${city??"near me"}`}
          onPointerDown={event=>{
            if ((event.target as HTMLElement).closest("button,a,.map-selected-card")) return;
            pointers.current.set(event.pointerId,{x:event.clientX,y:event.clientY});
            event.currentTarget.setPointerCapture(event.pointerId);
            if (pointers.current.size===1) {
              const world=toWorldPixels(center,zoom);
              dragging.current={id:event.pointerId,x:event.clientX,y:event.clientY,worldX:world.x,worldY:world.y};
            } else if (pointers.current.size===2) {
              const [first,second]=[...pointers.current.values()];
              const rect=event.currentTarget.getBoundingClientRect();
              pinch.current={view,midpoint:{x:(first.x+second.x)/2-rect.left,y:(first.y+second.y)/2-rect.top},
                distance:Math.hypot(second.x-first.x,second.y-first.y)};
              dragging.current=null;
            }
          }}
          onPointerMove={event=>{
            if (!pointers.current.has(event.pointerId)) return;
            pointers.current.set(event.pointerId,{x:event.clientX,y:event.clientY});
            if (pointers.current.size>=2 && pinch.current) {
              const [first,second]=[...pointers.current.values()];
              const rect=event.currentTarget.getBoundingClientRect();
              const midpoint={x:(first.x+second.x)/2-rect.left,y:(first.y+second.y)/2-rect.top};
              const distance=Math.hypot(second.x-first.x,second.y-first.y);
              setView(pinchMapView(pinch.current.view,rect.width,rect.height,
                pinch.current.midpoint,midpoint,distance/Math.max(1,pinch.current.distance)));
              return;
            }
            const drag=dragging.current;
            if (drag && drag.id===event.pointerId) setView(previous=>({...previous,
              center:fromWorldPixels({x:drag.worldX-event.clientX+drag.x,y:drag.worldY-event.clientY+drag.y},previous.zoom)}));
          }}
          onPointerUp={event=>{
            pointers.current.delete(event.pointerId);
            if (pointers.current.size<2) pinch.current=null;
            if (dragging.current?.id===event.pointerId) dragging.current=null;
          }}
          onPointerCancel={event=>{
            pointers.current.delete(event.pointerId);
            if (pointers.current.size<2) pinch.current=null;
            dragging.current=null;
          }}>
          <div className="map-tile-field" aria-hidden="true">
            {tiles.map(tile=><img key={`${zoom}-${tile.x}-${tile.y}`}
              src={`https://tile.openstreetmap.org/${zoom}/${tile.x}/${tile.y}.png`}
              alt="" width={256} height={256} draggable={false} loading="lazy"
              referrerPolicy="strict-origin-when-cross-origin"
              style={{left:tile.left,top:tile.top}} />)}
          </div>
          {shown.map(station=>{
            const at=position(station);
            const selected=active?.id===station.id;
            const remaining=nowMs===null?null:remainingChargingMinutes(station.availabilityForecast,nowMs);
            return <Fragment key={station.id}><button type="button" className={`map-pin status-${station.status.toLowerCase()}${selected?" selected":""}`}
              style={{left:at.left,top:at.top}} onClick={()=>{
                if (station.statusSource === "Device") router.push(`/driver/stations/${station.id}`);
                else setSelectedId(station.id);
              }}
              aria-label={`${station.name}, ${(sq?statusSq:statusEn)[station.status]}${remaining!==null?`, ${remaining} ${sq?"min të mbetura":"min remaining"}`:""}`}
              aria-pressed={selected} title={station.name}><span aria-hidden="true">ϟ</span></button>
              {station.status==="Charging"&&remaining!==null&&<span className="map-eta-badge" aria-hidden="true"
                style={{left:at.left+17,top:at.top-51}}>~{remaining} min</span>}</Fragment>;
          })}
          {driverLocation&&<div className="map-user-pin" role="img" aria-label={sq?"Vendndodhja ime":"My location"}
            style={position(driverLocation)}>●</div>}
          {active&&selectedId===active.id&&<div className="map-selected-card">
            <div><strong>{active.name}</strong><small>{(sq?statusSq:statusEn)[active.status]} · {active.powerKW} kW</small></div>
            <button type="button" onClick={()=>stationPanelRef.current?.scrollIntoView({behavior:"smooth",block:"start"})}>
              {sq?"Detajet":"Details"} ↓</button>
          </div>}
          <div className="map-city-label">{city ?
            (selectedCityHasStations?city:(sq?`Pa pika në ${city}`:`No stations in ${city}`)) :
            (driverLocation?(sq?"Vendndodhja ime":"My location"):(sq?"Shqipëri":"Albania"))}</div>
          <div className="map-zoom" aria-label={sq ? "Zmadhimi i hartës" : "Map zoom"}>
            <button type="button" onClick={()=>setView(previous=>({...previous,zoom:Math.min(19,previous.zoom+1)}))}
              disabled={zoom>=19} aria-label={sq?"Zmadho":"Zoom in"}>+</button>
            <button type="button" onClick={()=>setView(previous=>({...previous,zoom:Math.max(6,previous.zoom-1)}))}
              disabled={zoom<=6} aria-label={sq?"Zvogëlo":"Zoom out"}>−</button>
          </div>
          <span className="map-hint">{sq ? "Tërhiq për ta lëvizur · scroll për zmadhim" : "Drag to move · scroll to zoom"}</span>
          <a className="map-attribution" href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">© OpenStreetMap contributors</a>
        </div>
        <div className="station-panel" ref={stationPanelRef}>
          <div className="station-panel-head"><span className="driver-eyebrow">{sq ? "PIKA E ZGJEDHUR" : "SELECTED STATION"}</span>
            <strong>{active?.name ?? (sq ? "Zgjidh një pikë" : "Choose a station")}</strong></div>
          {active ? <>
            <div className={`station-status status-text-${active.status.toLowerCase()}`}><i/>{(sq?statusSq:statusEn)[active.status]}</div>
            <p className="station-address">{active.address}<br/>{active.city}</p>
            {driverLocation&&<p className="station-distance">{sq?"Largësia nga ti":"Distance from you"}: {distanceKm(driverLocation,active).toFixed(1)} km</p>}
            <a className="station-directions-link" href={directionsUrl(mapProvider, active)}
              target="_blank" rel="noopener noreferrer"
              aria-label={sq?`Hap udhëzimet për ${active.name} në ${mapProvider === "apple"?"Apple Maps":"Google Maps"}`:
                `Open directions to ${active.name} in ${mapProvider === "apple"?"Apple Maps":"Google Maps"}`}>
              {sq?"Hap navigimin në":"Open directions in"} {mapProvider === "apple"?"Apple Maps":"Google Maps"}
              <span aria-hidden="true">↗</span>
            </a>
            <AvailabilityCard station={active} language={language} nowMs={nowMs}/>
            <div className="station-specs">
              <div><span>{sq ? "FUQIA" : "POWER"}</span><strong>{active.powerKW} kW</strong></div>
              <div><span>{sq ? "LIDHËS" : "CONNECTORS"}</span><strong>{active.connectorCount} × {active.connectorType}</strong></div>
              <div><span>{sq ? "ÇMIMI" : "PRICE"}</span><strong>€{euro(active.pricePerKWh)} / kWh</strong></div>
              <div><span>ID</span><strong>{active.code}</strong></div>
            </div>
            {active.statusSource === "Device" && <Link href={`/driver/stations/${active.id}`} className="driver-history-link">
              {sq ? "Hap pikën e karikimit" : "Open charge point"} ↗
            </Link>}
            {active.isDemo&&<div className="station-footnote">{sq ? "Vendndodhja dhe gjendja janë të dhëna demo. Mos udhëto drejt kësaj pike pa e verifikuar." : "Location and status are demo data. Verify before travelling."}</div>}
          </> : <p className="station-address">{city ?
            (sq?`Nuk ka ende pika karikimi të regjistruara në ${city}.`:`No charging stations are registered in ${city} yet.`) :
            (sq ? "Nuk ka pika që përputhen me filtrin." : "No stations match this filter.")}</p>}
          <div className="station-list" aria-label={sq ? "Lista e pikave" : "Station list"}>
            {shown.map(station=><button type="button" key={station.id} onClick={()=>{
              if (station.statusSource === "Device") { router.push(`/driver/stations/${station.id}`); return; }
              setSelectedId(station.id);
              setView(previous=>({...previous,center:{latitude:station.latitude,longitude:station.longitude},zoom:Math.max(previous.zoom,13)}));
            }} aria-pressed={active?.id===station.id}>
              <span><strong>{station.name}</strong><small>{station.connectorType} · {station.powerKW} kW
                {driverLocation?` · ${distanceKm(driverLocation,station).toFixed(1)} km`:""}</small></span>
              <i className={`station-dot status-${station.status.toLowerCase()}`}/></button>)}
          </div>
        </div>
      </div>
      <div className="map-legend">{(["Available","Occupied","Charging","OutOfService","Maintenance","Unknown"] as const).map(status=>
        <span key={status}><i className={`station-dot status-${status.toLowerCase()}`}/>{(sq?statusSq:statusEn)[status]}</span>)}</div>
    </section>
  </div>;
}

