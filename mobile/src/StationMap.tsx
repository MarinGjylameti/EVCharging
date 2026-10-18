import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Animated, Image, Linking, PanResponder, Pressable, Text, View, type GestureResponderEvent } from "react-native";
import type { Language, Station } from "../../contracts/account";
import { fromWorldPixels, pinchMapView, toWorldPixels, validCoordinates, type Coordinates, type MapView, type WorldPoint } from "../../shared/map-geometry";
import { mapScene, mapTranslation } from "./map-scene";
import { fitMobileMap, initialMapView, type MapSession } from "./map-camera";
import { stationState } from "./station-data";
import { Button, Notice, colors } from "./ui";

const height = 360;
const tileHeaders = { "User-Agent": "EVCharging/0.1.0 (al.evcharging.mobile)" };
type Gesture = { view: MapView; points: WorldPoint[] };
const midpoint = (points: WorldPoint[]) => ({ x: (points[0].x + points[1].x) / 2, y: (points[0].y + points[1].y) / 2 });
const distance = (points: WorldPoint[]) => Math.hypot(points[0].x - points[1].x, points[0].y - points[1].y);

export function StationMap({ stations, language, stale, selectedId, onSelect, onInteraction, mapSession, location }: {
  stations: Station[]; language: Language; stale: boolean; selectedId?: string; onSelect: (station: Station) => void; onInteraction: (active: boolean) => void;
  mapSession?: MapSession; location?: Coordinates | null;
}) {
  const sq = language === "sq", mapRef = useRef<View>(null), origin = useRef({ x: 0, y: 0 });
  const localSession = useRef<MapSession>({}), session = mapSession ?? localSession.current;
  const [width, setWidth] = useState(0), [failed, setFailed] = useState(false), [attempt, setAttempt] = useState(0);
  const points = stations.filter(validCoordinates);
  const key = `${width}:${points.map(point => `${point.id}:${point.latitude}:${point.longitude}`).join(";")}`;
  const initial = initialMapView(points, location, width, height);
  const [moved, setMoved] = useState<{ key: string; view: MapView } | null>(null);
  const view = moved?.key === key ? moved.view : session.view ?? initial;
  const scene = mapScene(view, initial.center, width, height);
  const current = useRef({ view, key, width, origin: initial.center, sceneKey: scene.key });
  current.current = { view, key, width, origin: initial.center, sceneKey: scene.key };
  const live = useRef({ key, view });
  const latestView = () => live.current.key === current.current.key ? live.current.view : current.current.view;
  const [translation] = useState(() => new Animated.ValueXY({ x: 0, y: 0 }, { useNativeDriver: true }));
  const gesture = useRef<Gesture | null>(null);
  const touchStart = useRef<Gesture | null>(null);
  const pending = useRef<{ key: string; view: MapView } | null>(null), frame = useRef<number | null>(null);
  function flushMove() {
    if (frame.current !== null) cancelAnimationFrame(frame.current);
    frame.current = null;
    const next = pending.current; pending.current = null;
    if (next && next.key === current.current.key && (next.view.zoom !== current.current.view.zoom
      || mapScene(next.view, current.current.origin, current.current.width, height).key !== current.current.sceneKey)) setMoved(next);
  }
  function queueMove(next: MapView) {
    session.view = next;
    live.current = pending.current = { key: current.current.key, view: next };
    if (next.zoom === current.current.view.zoom) translation.setValue(mapTranslation(next, current.current.origin));
    if (frame.current === null) frame.current = requestAnimationFrame(flushMove);
  }
  const touchPoints = (event: GestureResponderEvent) => event.nativeEvent.touches.slice(0, 2).map(touch => ({ x: touch.pageX, y: touch.pageY }));
  const localMidpoint = (points: WorldPoint[]) => { const point = midpoint(points); return { x: point.x - origin.current.x, y: point.y - origin.current.y }; };
  function rebaseTouches(event: GestureResponderEvent) {
    const points = touchPoints(event);
    if (gesture.current && points.length && points.length !== gesture.current.points.length) gesture.current = { view: latestView(), points };
  }
  function finishGesture() {
    if (frame.current !== null) cancelAnimationFrame(frame.current);
    frame.current = null; pending.current = null;
    if (gesture.current && live.current.key === current.current.key) setMoved(live.current);
    gesture.current = null; touchStart.current = null; onInteraction(false);
  }
  useLayoutEffect(() => {
    if (live.current.key !== key) {
      live.current = { key, view };
      if (gesture.current || touchStart.current) onInteraction(false);
      gesture.current = null; touchStart.current = null;
    }
    if (width > 0 && !session.focused && !session.interacted && location !== undefined) {
      session.focused = true; session.view = initial; live.current = { key, view: initial }; setMoved(live.current);
    } else if (width > 0 && !session.view) session.view = view;
    const latest = latestView();
    translation.setValue(mapTranslation(latest.zoom === view.zoom ? latest : view, initial.center));
  });
  const responder = useMemo(() => PanResponder.create({
    onStartShouldSetPanResponder: event => event.nativeEvent.touches.length > 1,
    onMoveShouldSetPanResponder: (_event, state) => Math.abs(state.dx) + Math.abs(state.dy) > 3 || state.numberActiveTouches > 1,
    onPanResponderGrant: event => {
      session.interacted = true;
      const points = touchPoints(event);
      gesture.current = points.length === 1 && touchStart.current?.points.length === 1 ? touchStart.current : { view: latestView(), points };
      onInteraction(true);
    },
    onPanResponderStart: rebaseTouches,
    onPanResponderEnd: rebaseTouches,
    onPanResponderMove: event => {
      const touches = touchPoints(event), start = gesture.current;
      if (!touches.length) return;
      if (!start || touches.length !== start.points.length) { gesture.current = { view: latestView(), points: touches }; return; }
      let next: MapView;
      if (touches.length === 2) {
        next = pinchMapView(start.view, current.current.width, height, localMidpoint(start.points), localMidpoint(touches), distance(touches) / Math.max(1, distance(start.points)), true, 0);
      } else {
        const world = toWorldPixels(start.view.center, start.view.zoom);
        next = { zoom: start.view.zoom, center: fromWorldPixels({ x: world.x - touches[0].x + start.points[0].x, y: world.y - touches[0].y + start.points[0].y }, start.view.zoom) };
      }
      queueMove(next);
    },
    onPanResponderRelease: finishGesture,
    onPanResponderTerminate: finishGesture,
    onPanResponderTerminationRequest: () => false,
  }), [onInteraction]);
  useEffect(() => () => {
    if (frame.current !== null) cancelAnimationFrame(frame.current);
    frame.current = null; pending.current = null; gesture.current = null; onInteraction(false);
  }, [onInteraction]);
  const center = toWorldPixels(initial.center, view.zoom);
  function changeView(next: MapView) { finishGesture(); session.interacted = true; session.view = next; live.current = { key, view: next }; setMoved(live.current); }
  const zoom = (delta: number) => { const latest = latestView(); changeView({ ...latest, zoom: Math.max(0, Math.min(19, latest.zoom + delta)) }); };
  const locationPoint = location && validCoordinates(location) ? toWorldPixels(location, view.zoom) : null;
  return <View style={{ gap: 10 }}>
    <View ref={mapRef} onLayout={event => setWidth(event.nativeEvent.layout.width)}
      onTouchStart={event => {
        session.interacted = true;
        if (event.nativeEvent.touches.length === 1) {
          mapRef.current?.measureInWindow((x, y) => { origin.current = { x, y }; });
          touchStart.current = { view: latestView(), points: touchPoints(event) };
        }
        onInteraction(true);
      }}
      onTouchEnd={event => { if (!event.nativeEvent.touches.length) finishGesture(); }} onTouchCancel={finishGesture}
      style={{ height, overflow: "hidden", borderRadius: 22, borderWidth: 1, borderColor: colors.border, backgroundColor: "#e5ebdf" }} {...responder.panHandlers}>
      <Animated.View pointerEvents="box-none" style={{ position: "absolute", left: scene.left, top: scene.top, width: scene.right - scene.left, height: scene.bottom - scene.top, transform: translation.getTranslateTransform() }}>
        <View pointerEvents="none" style={{ position: "absolute", inset: 0 }}>{scene.tiles.map(tile => <Image key={`${attempt}:${scene.zoom}:${tile.x}:${tile.y}`} accessible={false} fadeDuration={0}
          source={{ uri: `https://tile.openstreetmap.org/${scene.zoom}/${tile.x}/${tile.y}.png`, headers: tileHeaders, cache: "default" }}
          style={{ position: "absolute", left: tile.left - scene.left, top: tile.top - scene.top, width: scene.size, height: scene.size }} onError={() => setFailed(true)}/>)}</View>
      {width > 0 && points.map(station => {
        const point = toWorldPixels(station, view.zoom), left = point.x - center.x + width / 2 - 22, top = point.y - center.y + height / 2 - 22;
        if (left < scene.left - 44 || left > scene.right || top < scene.top - 44 || top > scene.bottom) return null;
        const status = stationState(station.status, language);
        const selected = selectedId === station.id;
        return <Pressable key={station.id} accessibilityRole="button" accessibilityState={{ selected }} accessibilityLabel={`${station.name}, ${stale ? (sq ? "gjendja e fundit: " : "last status: ") : ""}${status.text}. ${sq ? "Shiko detajet" : "View details"}`} onPress={() => onSelect(station)}
          style={{ position: "absolute", left: left - scene.left, top: top - scene.top, width: 44, height: 44, zIndex: selected ? 2 : 1, alignItems: "center", justifyContent: "center" }}>
          <View style={{ width: selected ? 44 : 40, height: selected ? 44 : 40, borderRadius: 22, borderWidth: selected ? 4 : 3, borderColor: selected ? colors.lime : "white", backgroundColor: stale ? colors.muted : status.color, alignItems: "center", justifyContent: "center", elevation: 3, shadowColor: "#153b2d", shadowOpacity: .28, shadowRadius: 3, shadowOffset: { width: 0, height: 2 } }}><Text style={{ color: "white", fontSize: 22, fontWeight: "800" }}>{station.status === "OutOfService" ? "×" : station.status === "Maintenance" ? "Ⅱ" : station.status === "Unknown" ? "?" : "ϟ"}</Text></View>
        </Pressable>;
      })}
      {width > 0 && locationPoint && <View pointerEvents="none" accessible accessibilityLabel={sq ? "Vendndodhja jote në hapje" : "Your location at opening"}
        style={{ position: "absolute", left: locationPoint.x - center.x + width / 2 - 12 - scene.left, top: locationPoint.y - center.y + height / 2 - 12 - scene.top, width: 24, height: 24, borderRadius: 12, borderWidth: 5, borderColor: "#83c8ff88", zIndex: 3, backgroundColor: "white", alignItems: "center", justifyContent: "center" }}>
        <View style={{ width: 10, height: 10, borderRadius: 5, backgroundColor: "#087cf0" }}/>
      </View>}
      </Animated.View>
      <View style={{ position: "absolute", zIndex: 3, right: 10, top: 10, borderRadius: 14, overflow: "hidden", backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border }}>{([1, -1] as const).map(delta => <Pressable key={delta} accessibilityRole="button" accessibilityLabel={delta > 0 ? (sq ? "Afro hartën" : "Zoom in") : (sq ? "Largo hartën" : "Zoom out")} onPress={() => zoom(delta)} style={{ width: 44, height: 44, alignItems: "center", justifyContent: "center", borderTopWidth: delta < 0 ? 1 : 0, borderTopColor: colors.border }}><Text style={{ color: colors.ink, fontSize: 24 }}>{delta > 0 ? "+" : "−"}</Text></Pressable>)}</View>
      <Pressable accessibilityRole="button" accessibilityLabel={sq ? "Shfaq të gjitha pikat në hartë" : "Fit stations on map"} onPress={() => changeView(fitMobileMap(location ? [...points, location] : points, width, height))} style={{ position: "absolute", zIndex: 3, left: 10, bottom: 10, width: 44, height: 44, borderRadius: 14, backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border, alignItems: "center", justifyContent: "center" }}><Text style={{ fontSize: 27, color: colors.ink }}>⤢</Text></Pressable>
      <Pressable accessibilityRole="link" onPress={() => void Linking.openURL("https://www.openstreetmap.org/copyright").catch(() => setFailed(true))} style={{ position: "absolute", zIndex: 3, right: 0, bottom: 0, minHeight: 44, maxWidth: "76%", paddingHorizontal: 8, justifyContent: "center", backgroundColor: "#fffffff2" }}><Text style={{ color: colors.ink, fontSize: 10 }}>© OpenStreetMap contributors</Text></Pressable>
    </View>
    {!stale && <View style={{ flexDirection: "row", flexWrap: "wrap", gap: 12, paddingHorizontal: 4 }}>{[...new Set(points.map(point => point.status))].map(value => {
      const status = stationState(value, language);
      return <View key={value} style={{ flexDirection: "row", alignItems: "center", gap: 5 }}><View style={{ width: 9, height: 9, borderRadius: 5, backgroundColor: status.color }}/><Text style={{ fontSize: 11, color: colors.ink }}>{status.text}</Text></View>;
    })}</View>}
    {failed && <><Notice text={sq ? "Harta nuk u ngarkua plotësisht. Mund të përdorësh edhe listën e pikave." : "The map did not load completely. You can still use the station list."}/><Button secondary title={sq ? "Riprovo hartën" : "Retry map"} onPress={() => { setFailed(false); setAttempt(value => value + 1); }}/></>}
    {points.length !== stations.length && <Notice text={sq ? "Disa pika nuk kanë koordinata të vlefshme. Shikoji te Lista." : "Some stations have no valid coordinates. Find them in List."}/>}
  </View>;
}
