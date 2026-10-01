"use client";

import { useEffect, useRef, useState } from "react";
import type { Language } from "../../../../contracts/account";
import { coordinateAtPixel, fromWorldPixels, toWorldPixels, visibleTiles, zoomAroundPixel, type Coordinates } from "@/lib/map-geometry";

type Props = {
  initial: Coordinates;
  language: Language;
  requireClick: boolean;
  onConfirm: (point: Coordinates) => void;
  onClose: () => void;
};

const rounded = (point: Coordinates): Coordinates => ({
  latitude: Number(point.latitude.toFixed(6)),
  longitude: Number(point.longitude.toFixed(6))
});

export function StationMapPicker({initial, language, requireClick, onConfirm, onClose}: Props) {
  const sq = language === "sq";
  const [view, setView] = useState({center:initial, zoom:requireClick ? 8 : 16});
  const {center, zoom} = view;
  const [candidate, setCandidate] = useState(initial);
  const [picked, setPicked] = useState(!requireClick);
  const [size, setSize] = useState({width:0,height:0});
  const stageRef = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const drag = useRef<{id:number;x:number;y:number;worldX:number;worldY:number;moved:boolean}|null>(null);
  const wheelDelta = useRef(0);

  useEffect(() => {
    closeRef.current?.focus();
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => { document.body.style.overflow = previousOverflow; };
  }, []);
  useEffect(() => {
    const stage = stageRef.current;
    if (!stage) return;
    const observer = new ResizeObserver(() => setSize({width:stage.clientWidth,height:stage.clientHeight}));
    observer.observe(stage);
    setSize({width:stage.clientWidth,height:stage.clientHeight});
    return () => observer.disconnect();
  }, []);
  useEffect(() => {
    const stage = stageRef.current;
    if (!stage) return;
    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      const delta = event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? stage.clientHeight : 1);
      wheelDelta.current += delta;
      if (Math.abs(wheelDelta.current) < 60) return;
      const direction = wheelDelta.current > 0 ? -1 : 1;
      wheelDelta.current = 0;
      const rect = stage.getBoundingClientRect();
      const x = event.clientX - rect.left, y = event.clientY - rect.top;
      setView(previous => {
        const nextZoom = Math.max(6, Math.min(19, previous.zoom + direction));
        if (nextZoom === previous.zoom) return previous;
        return {zoom:nextZoom, center:zoomAroundPixel(previous.center, previous.zoom, nextZoom,
          rect.width, rect.height, x, y)};
      });
    };
    stage.addEventListener("wheel", onWheel, {passive:false});
    return () => stage.removeEventListener("wheel", onWheel);
  }, []);

  const tiles = visibleTiles(center, zoom, size.width, size.height);
  const centerWorld = toWorldPixels(center, zoom);
  const markerWorld = toWorldPixels(candidate, zoom);
  const marker = {left:markerWorld.x-centerWorld.x+size.width/2,top:markerWorld.y-centerWorld.y+size.height/2};

  function selectAt(clientX: number, clientY: number) {
    const rect = stageRef.current?.getBoundingClientRect();
    if (!rect) return;
    const selected = rounded(coordinateAtPixel(center, zoom, rect.width, rect.height,
      clientX-rect.left, clientY-rect.top));
    setCandidate(selected);
    setView(previous => ({...previous,center:selected}));
    setPicked(true);
  }
  function moveCenter(dx: number, dy: number, worldX: number, worldY: number) {
    setView(previous => ({...previous,center:fromWorldPixels({x:worldX-dx,y:worldY-dy},previous.zoom)}));
  }
  function handleKey(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape") { event.preventDefault(); onClose(); return; }
    if (event.key !== "Tab") return;
    const focusable = dialogRef.current?.querySelectorAll<HTMLElement>("button:not(:disabled), [tabindex]:not([tabindex='-1'])");
    if (!focusable?.length) return;
    const first = focusable[0], last = focusable[focusable.length-1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }
  function handleMapKey(event: React.KeyboardEvent<HTMLDivElement>) {
    const offset: Record<string,[number,number]> = {
      ArrowLeft:[80,0], ArrowRight:[-80,0], ArrowUp:[0,80], ArrowDown:[0,-80]
    };
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setCandidate(rounded(center)); setPicked(true);
    } else if (offset[event.key]) {
      event.preventDefault();
      const [x,y] = offset[event.key];
      moveCenter(x,y,centerWorld.x,centerWorld.y);
    }
  }

  return <div className="station-picker-backdrop" onMouseDown={event=>{if(event.target===event.currentTarget)onClose();}}>
    <div className="station-picker-dialog" role="dialog" aria-modal="true" aria-labelledby="station-picker-title"
      ref={dialogRef} onKeyDown={handleKey}>
      <div className="station-picker-header"><div><span className="eyebrow">{sq?"VENDNDODHJA E KARIKUESIT":"CHARGER LOCATION"}</span>
        <h2 id="station-picker-title">{sq?"Zgjidh pikën në hartë":"Choose a point on the map"}</h2>
        <p>{sq?"Lëvize hartën, zmadhoje dhe kliko vendin e saktë. Kliko sërish pas zmadhimit për më shumë saktësi.":
          "Move and zoom the map, then click the exact location. Click again after zooming for greater precision."}</p></div>
        <button type="button" className="station-picker-close" ref={closeRef} onClick={onClose}
          aria-label={sq?"Mbyll hartën":"Close map"}>×</button></div>
      <div className="station-picker-map" ref={stageRef} role="application" tabIndex={0}
        aria-label={sq?"Harta për zgjedhjen e vendndodhjes. Përdor shigjetat për lëvizje dhe Enter për të zgjedhur qendrën.":
          "Location map. Use arrow keys to move and Enter to select the center."}
        onKeyDown={handleMapKey}
        onPointerDown={event=>{
          if ((event.target as HTMLElement).closest("button,a")) return;
          const world = toWorldPixels(center,zoom);
          drag.current={id:event.pointerId,x:event.clientX,y:event.clientY,worldX:world.x,worldY:world.y,moved:false};
          event.currentTarget.setPointerCapture(event.pointerId);
        }}
        onPointerMove={event=>{
          const current=drag.current;
          if (!current||current.id!==event.pointerId) return;
          const dx=event.clientX-current.x,dy=event.clientY-current.y;
          if (Math.hypot(dx,dy)>5) current.moved=true;
          if (current.moved) moveCenter(dx,dy,current.worldX,current.worldY);
        }}
        onPointerUp={event=>{
          const current=drag.current;
          if (!current||current.id!==event.pointerId) return;
          drag.current=null;
          if (!current.moved) selectAt(event.clientX,event.clientY);
        }}
        onPointerCancel={()=>{drag.current=null;}}>
        <div className="station-picker-tiles" aria-hidden="true">
          {tiles.map(tile=><img key={`${zoom}-${tile.x}-${tile.y}`}
            src={`https://tile.openstreetmap.org/${zoom}/${tile.x}/${tile.y}.png`}
            alt="" width={256} height={256} draggable={false} loading="lazy"
            referrerPolicy="strict-origin-when-cross-origin"
            style={{left:tile.left,top:tile.top}}/>)}
        </div>
        {picked&&<div className="station-picker-marker" style={marker} aria-hidden="true"><span>ϟ</span></div>}
        <div className="station-picker-zoom" aria-label={sq?"Zmadhimi":"Zoom"}>
          <button type="button" onClick={()=>setView(previous=>({...previous,zoom:Math.min(previous.zoom+1,19)}))} disabled={zoom>=19} aria-label={sq?"Zmadho":"Zoom in"}>+</button>
          <button type="button" onClick={()=>setView(previous=>({...previous,zoom:Math.max(previous.zoom-1,6)}))} disabled={zoom<=6} aria-label={sq?"Zvogëlo":"Zoom out"}>−</button>
        </div>
        <div className="station-picker-crosshair" aria-hidden="true">+</div>
        <a className="station-picker-attribution" href="https://www.openstreetmap.org/copyright" target="_blank"
          rel="noopener noreferrer">© OpenStreetMap contributors</a>
      </div>
      <div className="station-picker-footer"><div><strong>{picked?(sq?"Pika e zgjedhur":"Selected point"):(sq?"Kliko hartën për të zgjedhur":"Click the map to choose")}</strong>
        <span>{picked?`${candidate.latitude.toFixed(6)}, ${candidate.longitude.toFixed(6)}`:
          (sq?"Koordinatat shfaqen këtu":"Coordinates will appear here")}</span></div>
        <div className="station-picker-actions"><button type="button" className="button-outline" onClick={onClose}>{sq?"Anulo":"Cancel"}</button>
          <button type="button" className="station-picker-confirm" disabled={!picked}
            onClick={()=>onConfirm(candidate)}>{sq?"Konfirmo vendndodhjen":"Confirm location"}</button></div></div>
    </div>
  </div>;
}
