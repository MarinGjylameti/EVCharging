import { useEffect, useMemo, useRef, useState } from "react";
import { Image, Linking, PanResponder, Pressable, Text, View, type GestureResponderEvent } from "react-native";
import type { Language, Station } from "../../contracts/account";
import { fitMapView, fromWorldPixels, pinchMapView, toWorldPixels, validCoordinates, visibleTiles, type MapView, type WorldPoint } from "../../shared/map-geometry";
import { stationState } from "./station-data";
import { Button, Notice, colors } from "./ui";

const height = 360;
const tileHeaders = { "User-Agent": "EVCharging/0.1.0 (al.evcharging.mobile)" };
type Gesture = { view: MapView; points: WorldPoint[] };
const midpoint = (points: WorldPoint[]) => ({ x: (points[0].x + points[1].x) / 2, y: (points[0].y + points[1].y) / 2 });
const distance = (points: WorldPoint[]) => Math.hypot(points[0].x - points[1].x, points[0].y - points[1].y);

export function StationMap({ stations, language, stale, selectedId, onSelect, onInteraction }: {
  stations: Station[]; language: Language; stale: boolean; selectedId?: string; onSelect: (station: Station) => void; onInteraction: (active: boolean) => void;
}) {
  const sq = language === "sq", mapRef = useRef<View>(null), origin = useRef({ x: 0, y: 0 });
  const [width, setWidth] = useState(0), [failed, setFailed] = useState(false), [attempt, setAttempt] = useState(0);
  const points = stations.filter(validCoordinates);
  const key = `${width}:${points.map(point => `${point.id}:${point.latitude}:${point.longitude}`).join(";")}`;
  const initial = fitMapView(points, width, height);
  const [moved, setMoved] = useState<{ key: string; view: MapView } | null>(null);
  const view = moved?.key === key ? moved.view : initial;
  const current = useRef({ view, key, width }); current.current = { view, key, width };
  const gesture = useRef<Gesture | null>(null);
  const touchPoints = (event: GestureResponderEvent) => event.nativeEvent.touches.slice(0, 2).map(touch => ({ x: touch.pageX - origin.current.x, y: touch.pageY - origin.current.y }));
  const responder = useMemo(() => PanResponder.create({
    onMoveShouldSetPanResponder: (_event, state) => Math.abs(state.dx) + Math.abs(state.dy) > 5 || state.numberActiveTouches > 1,
    onPanResponderGrant: event => { gesture.current = { view: current.current.view, points: touchPoints(event) }; onInteraction(true); },
    onPanResponderMove: event => {
      const touches = touchPoints(event), start = gesture.current;
      if (!touches.length) return;
      if (!start || touches.length !== start.points.length) { gesture.current = { view: current.current.view, points: touches }; return; }
      let next: MapView;
      if (touches.length === 2) {
        next = pinchMapView(start.view, current.current.width, height, midpoint(start.points), midpoint(touches), distance(touches) / Math.max(1, distance(start.points)));
      } else {
        const world = toWorldPixels(start.view.center, start.view.zoom);
        next = { zoom: start.view.zoom, center: fromWorldPixels({ x: world.x - touches[0].x + start.points[0].x, y: world.y - touches[0].y + start.points[0].y }, start.view.zoom) };
      }
      setMoved({ key: current.current.key, view: next });
    },
    onPanResponderRelease: () => { gesture.current = null; onInteraction(false); },
    onPanResponderTerminate: () => { gesture.current = null; onInteraction(false); },
    onPanResponderTerminationRequest: () => false,
  }), [onInteraction]);
  useEffect(() => () => onInteraction(false), [onInteraction]);
  const center = toWorldPixels(view.center, view.zoom);
  const zoom = (delta: number) => setMoved({ key, view: { ...view, zoom: Math.max(6, Math.min(19, view.zoom + delta)) } });
  return <View style={{ gap: 10 }}>
    <View ref={mapRef} onLayout={event => setWidth(event.nativeEvent.layout.width)}
      onTouchStart={() => { mapRef.current?.measureInWindow((x, y) => { origin.current = { x, y }; }); onInteraction(true); }}
      onTouchEnd={event => { if (!event.nativeEvent.touches.length) onInteraction(false); }} onTouchCancel={() => onInteraction(false)}
      style={{ height, overflow: "hidden", borderRadius: 22, borderWidth: 1, borderColor: colors.border, backgroundColor: "#e5ebdf" }} {...responder.panHandlers}>
      <View pointerEvents="none" style={{ position: "absolute", inset: 0 }}>
        {visibleTiles(view.center, view.zoom, width, height).map(tile => <Image key={`${attempt}:${view.zoom}:${tile.x}:${tile.y}`} accessible={false}
          source={{ uri: `https://tile.openstreetmap.org/${view.zoom}/${tile.x}/${tile.y}.png`, headers: tileHeaders, cache: "default" }}
          style={{ position: "absolute", left: tile.left, top: tile.top, width: 256, height: 256 }} onError={() => setFailed(true)}/>)}
      </View>
      {width > 0 && points.map(station => {
        const point = toWorldPixels(station, view.zoom), left = point.x - center.x + width / 2 - 22, top = point.y - center.y + height / 2 - 22;
        if (left < -44 || left > width || top < -44 || top > height) return null;
        const status = stationState(station.status, language);
        const selected = selectedId === station.id;
        return <Pressable key={station.id} accessibilityRole="button" accessibilityState={{ selected }} accessibilityLabel={`${station.name}, ${stale ? (sq ? "gjendja e fundit: " : "last status: ") : ""}${status.text}. ${sq ? "Shiko detajet" : "View details"}`} onPress={() => onSelect(station)}
          style={{ position: "absolute", left, top, width: 44, height: 44, zIndex: selected ? 2 : 1, alignItems: "center", justifyContent: "center" }}>
          <View style={{ width: selected ? 42 : 34, height: selected ? 42 : 34, borderRadius: 22, borderWidth: selected ? 4 : 3, borderColor: selected ? colors.lime : "white", backgroundColor: stale ? colors.muted : status.color, alignItems: "center", justifyContent: "center", elevation: 3, shadowColor: "#153b2d", shadowOpacity: .22, shadowRadius: 3, shadowOffset: { width: 0, height: 2 } }}><Text style={{ color: "white", fontSize: 21, fontWeight: "800" }}>ϟ</Text></View>
        </Pressable>;
      })}
      <View style={{ position: "absolute", zIndex: 3, right: 10, top: 10, borderRadius: 14, overflow: "hidden", backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border }}>{([1, -1] as const).map(delta => <Pressable key={delta} accessibilityRole="button" accessibilityLabel={delta > 0 ? (sq ? "Afro hartën" : "Zoom in") : (sq ? "Largo hartën" : "Zoom out")} onPress={() => zoom(delta)} style={{ width: 44, height: 44, alignItems: "center", justifyContent: "center", borderTopWidth: delta < 0 ? 1 : 0, borderTopColor: colors.border }}><Text style={{ color: colors.ink, fontSize: 24 }}>{delta > 0 ? "+" : "−"}</Text></Pressable>)}</View>
      <Pressable accessibilityRole="button" accessibilityLabel={sq ? "Shfaq të gjitha pikat në hartë" : "Fit stations on map"} onPress={() => setMoved(null)} style={{ position: "absolute", zIndex: 3, left: 10, bottom: 10, width: 44, height: 44, borderRadius: 14, backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border, alignItems: "center", justifyContent: "center" }}><Text style={{ fontSize: 27, color: colors.ink }}>⤢</Text></Pressable>
      <Pressable accessibilityRole="link" onPress={() => void Linking.openURL("https://www.openstreetmap.org/copyright").catch(() => setFailed(true))} style={{ position: "absolute", zIndex: 3, right: 0, bottom: 0, minHeight: 44, maxWidth: "76%", paddingHorizontal: 8, justifyContent: "center", backgroundColor: "#fffffff2" }}><Text style={{ color: colors.ink, fontSize: 10 }}>© OpenStreetMap contributors</Text></Pressable>
    </View>
    {failed && <><Notice text={sq ? "Harta nuk u ngarkua plotësisht. Mund të përdorësh edhe listën e pikave." : "The map did not load completely. You can still use the station list."}/><Button secondary title={sq ? "Riprovo hartën" : "Retry map"} onPress={() => { setFailed(false); setAttempt(value => value + 1); }}/></>}
    {points.length !== stations.length && <Notice text={sq ? "Disa pika nuk kanë koordinata të vlefshme. Shikoji te Lista." : "Some stations have no valid coordinates. Find them in List."}/>}
  </View>;
}
