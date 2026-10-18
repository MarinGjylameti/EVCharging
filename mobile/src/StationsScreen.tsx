import { useEffect, useMemo, useRef, useState } from "react";
import { Linking, Platform, Pressable, StyleSheet, Text, View, type ScrollView } from "react-native";
import type { Language, Station } from "../../contracts/account";
import { messages, errorText } from "./i18n";
import { navigationUrls, selectStations } from "./station-data";
import { readMapProvider } from "./map-preference";
import { StationMap } from "./StationMap";
import { readInitialLocation } from "./map-location";
import type { MapSession } from "./map-camera";
import { StationSummaryCard } from "./StationSummaryCard";
import { Button, Card, ChoiceField, Chip, Notice, Screen, colors, styles } from "./ui";

export function StationsScreen({ stations, language, refresh, onSelect, stale = false, mapSession }: { stations: Station[]; language: Language; refresh: () => Promise<void>; onSelect: (stationId: string) => void; stale?: boolean; mapSession?: MapSession }) {
  const t = messages[language], sq = language === "sq";
  const localSession = useRef<MapSession>({}), session = mapSession ?? localSession.current;
  const [location, setLocation] = useState(session.location);
  useEffect(() => {
    let mounted = true;
    void readInitialLocation(session).then(point => { if (mounted) setLocation(point); });
    return () => { mounted = false; };
  }, [session]);
  const [city, setCity] = useState("");
  const [availableOnly, setAvailableOnly] = useState(false);
  const [power, setPower] = useState<"all" | "ac" | "fast">("all");
  const [error, setError] = useState("");
  const [mode, setMode] = useState<"map" | "list">("map"), [interacting, setInteracting] = useState(false);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null | undefined>(undefined);
  const [expandedListId, setExpandedListId] = useState<string | null>(null);
  const scrollRef = useRef<ScrollView>(null), detailY = useRef<number | null>(null), pendingReveal = useRef(false);
  const navigating = useRef(false);
  const cities = useMemo(() => [...new Set(stations.filter(item => !item.isArchived).map(item => item.city))].sort(), [stations]);
  const shown = selectStations(stations, city, availableOnly, power);
  const selected = selectedId === undefined && shown.length === 1 ? shown[0] : shown.find(item => item.id === selectedId) ?? null;
  const filterCount = Number(!!city) + Number(availableOnly) + Number(power !== "all");
  const countLabel = sq ? shown.length === 1 ? "1 pikë" : shown.length + " pika" : shown.length === 1 ? "1 station" : shown.length + " stations";
  const summary = [city, power === "ac" ? "≤ 22 kW" : power === "fast" ? "> 22 kW" : "", availableOnly ? t.availableOnly : ""].filter(Boolean).join(" · ");
  function clearSelection() { setSelectedId(undefined); setExpandedListId(null); detailY.current = null; pendingReveal.current = false; }
  function resetFilters() { setCity(""); setPower("all"); setAvailableOnly(false); clearSelection(); }
  function revealDetails(y: number) { scrollRef.current?.scrollTo({ y: Math.max(0, y - 100), animated: true }); }
  function selectPoint(station: Station) {
    if (selected?.id === station.id && detailY.current !== null) { revealDetails(detailY.current); return; }
    pendingReveal.current = true; setSelectedId(station.id);
  }
  async function openMap(station: Station) {
    if (navigating.current) return;
    navigating.current = true;
    try {
      const provider = await readMapProvider();
      const urls = navigationUrls(station, provider, Platform.OS === "ios");
      try { await Linking.openURL(urls.primary); } catch { await Linking.openURL(urls.fallback); }
      setError("");
    } catch (failure) { setError(errorText(failure, language)); }
    finally { navigating.current = false; }
  }
  return <Screen language={language} refresh={refresh} scrollEnabled={!interacting} scrollRef={scrollRef}>
    <View style={layout.heading}>
      <View style={{ flex: 1, gap: 5 }}><Text accessibilityRole="header" style={layout.title}>{t.stations}</Text><Text style={layout.subtitle}>{sq ? "Zgjidh pikën. Shiko detajet. Kariko." : "Choose a station. View details. Charge."}</Text></View>
      <View style={layout.networkIcon} accessible={false}><Text style={layout.bolt}>ϟ</Text></View>
    </View>
    <View style={layout.toolbar}>
      <View style={layout.segments}>{(["map", "list"] as const).map(value => <Pressable key={value} accessibilityRole="tab" accessibilityState={{ selected: mode === value }}
        accessibilityLabel={value === "map" ? (sq ? "Harta" : "Map") : (sq ? "Lista" : "List")} onPress={() => setMode(value)}
        style={[layout.segment, mode === value && layout.segmentActive]}>
        <Text style={[layout.segmentText, mode === value && { color: colors.white }]}>{value === "map" ? (sq ? "Harta" : "Map") : (sq ? "Lista" : "List")}</Text>
      </Pressable>)}</View>
      <Pressable accessibilityRole="button" accessibilityState={{ expanded: filtersOpen }} accessibilityLabel={sq ? "Filtrat" : "Filters"} onPress={() => setFiltersOpen(value => !value)} style={[layout.filterButton, filterCount > 0 && { borderColor: colors.green }]}>
        <View accessible={false} style={layout.filterIcon}>{[18, 12, 6].map(width => <View key={width} style={{ width, height: 2, backgroundColor: colors.ink, borderRadius: 1 }}/>)}</View>
        <Text style={layout.segmentText}>{sq ? "Filtrat" : "Filters"}{filterCount ? " · " + filterCount : ""}</Text>
      </Pressable>
    </View>
    {filtersOpen && <View style={layout.filters}>
      <ChoiceField label={sq ? "Qyteti" : "City"} value={city} options={[{ value: "", label: t.allCities }, ...cities.map(value => ({ value, label: value }))]} onChange={value => { setCity(value); clearSelection(); }} language={language}/>
      <ChoiceField label={sq ? "Fuqia e pikës" : "Charger rating"} value={power} options={[{ value: "all", label: sq ? "Çdo fuqi" : "Any rating" }, { value: "ac", label: "≤ 22 kW" }, { value: "fast", label: "> 22 kW" }]} onChange={value => { setPower(value as typeof power); clearSelection(); }} language={language}/>
      <Chip title={t.availableOnly} selected={availableOnly} onPress={() => { setAvailableOnly(value => !value); clearSelection(); }}/>
      <Button title={(sq ? "Shfaq " : "Show ") + countLabel} onPress={() => setFiltersOpen(false)}/>
      {filterCount > 0 && <Button title={sq ? "Pastro filtrat" : "Reset filters"} secondary onPress={resetFilters}/>}
    </View>}
    <View style={layout.results}><Text style={layout.resultCount}>{countLabel}</Text>{!!summary && <Text style={layout.summary}>{summary}</Text>}</View>
    {!!error && <Notice text={error}/>}
    {stale && <Notice text={sq ? "Lidhja është ndërprerë. Gjendjet e pikave janë nga përditësimi i fundit." : "Connection lost. Station statuses are from the last update."}/>}
    {shown.length > 0 ? mode === "map" ? <>
      <StationMap stations={shown} language={language} stale={stale} selectedId={selected?.id} onSelect={selectPoint} onInteraction={setInteracting} mapSession={session} location={location}/>
      {location === null && <Text style={layout.mapHint}>{sq ? "Vendndodhja nuk është e disponueshme. Mund ta lëvizësh hartën për të zgjedhur pikën." : "Location is unavailable. You can move the map to choose a station."}</Text>}
      {selected ? <View key={selected.id} onLayout={event => {
        detailY.current = event.nativeEvent.layout.y;
        if (pendingReveal.current) { pendingReveal.current = false; revealDetails(detailY.current); }
      }}><StationSummaryCard station={selected} language={language} stale={stale} onOpen={() => onSelect(selected.id)} onNavigate={() => void openMap(selected)} onClose={() => { setSelectedId(null); pendingReveal.current = false; }}/></View>
        : <Text style={layout.mapHint}>{sq ? "Prek një pikë në hartë për detajet dhe karikimin." : "Tap a station on the map for details and charging."}</Text>}
    </> : shown.map(station => <StationSummaryCard key={station.id} station={station} language={language} stale={stale}
      actionsVisible={shown.length === 1 || expandedListId === station.id}
      onToggleActions={shown.length > 1 ? () => setExpandedListId(current => current === station.id ? null : station.id) : undefined}
      onOpen={() => onSelect(station.id)} onNavigate={() => void openMap(station)}/>)
      : <Card><Text style={styles.body}>{stations.some(item => !item.isArchived) ? t.noStations : (sq ? "Ende nuk ka pika të publikuara. Provo përsëri më vonë." : "No stations have been published yet. Please try again later.")}</Text>{filterCount > 0 && <Button title={sq ? "Pastro filtrat" : "Reset filters"} secondary onPress={resetFilters}/>}</Card>}
  </Screen>;
}

const layout = StyleSheet.create({
  heading: { flexDirection: "row", alignItems: "center", gap: 12, paddingVertical: 4 },
  title: { fontSize: 30, fontWeight: "700", letterSpacing: -1, color: colors.ink },
  subtitle: { fontSize: 13, lineHeight: 20, color: colors.muted },
  networkIcon: { width: 48, height: 48, borderRadius: 16, backgroundColor: colors.lime, alignItems: "center", justifyContent: "center" },
  bolt: { color: colors.ink, fontSize: 30, fontWeight: "700" },
  toolbar: { flexDirection: "row", flexWrap: "wrap", gap: 10, alignItems: "center" },
  segments: { flexDirection: "row", flexGrow: 1, flexBasis: 160, borderRadius: 16, backgroundColor: "#e9eee4", padding: 4 },
  segment: { flex: 1, minHeight: 44, paddingHorizontal: 12, paddingVertical: 10, borderRadius: 12, justifyContent: "center", alignItems: "center" },
  segmentActive: { backgroundColor: colors.ink },
  segmentText: { fontSize: 14, fontWeight: "600", color: colors.ink },
  filterButton: { minHeight: 52, flexDirection: "row", alignItems: "center", justifyContent: "center", gap: 9, borderRadius: 15, paddingHorizontal: 14, backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border },
  filterIcon: { width: 18, gap: 4, alignItems: "center" },
  filters: { borderRadius: 20, padding: 16, gap: 8, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.white },
  results: { flexDirection: "row", flexWrap: "wrap", alignItems: "center", gap: 8 },
  resultCount: { fontSize: 13, fontWeight: "700", color: colors.ink },
  summary: { fontSize: 12, color: colors.muted, flexShrink: 1 },
  mapHint: { fontSize: 13, lineHeight: 20, color: colors.muted, textAlign: "center", paddingHorizontal: 12 },
});
