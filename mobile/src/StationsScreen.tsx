import { useMemo, useState } from "react";
import { Linking, Platform, Text, View } from "react-native";
import type { Language, Station } from "../../contracts/account";
import { messages, errorText } from "./i18n";
import { navigationUrls, selectStations, stationState } from "./station-data";
import { Button, Card, ChoiceField, Chip, Notice, Screen, styles } from "./ui";

export function StationsScreen({ stations, language, refresh, onSelect, stale = false }: { stations: Station[]; language: Language; refresh: () => Promise<void>; onSelect: (stationId: string) => void; stale?: boolean }) {
  const t = messages[language]; const sq = language === "sq";
  const [city, setCity] = useState("");
  const [availableOnly, setAvailableOnly] = useState(false);
  const [power, setPower] = useState<"all" | "ac" | "fast">("all");
  const [error, setError] = useState("");
  const cities = useMemo(() => [...new Set(stations.filter(item => !item.isArchived).map(item => item.city))].sort(), [stations]);
  const shown = selectStations(stations, city, availableOnly, power);
  async function openMap(station: Station) {
    try {
      const urls = navigationUrls(station, Platform.OS === "ios");
      try { await Linking.openURL(urls.primary); } catch { await Linking.openURL(urls.fallback); }
      setError("");
    } catch (failure) { setError(errorText(failure, language)); }
  }
  return <Screen language={language} refresh={refresh}>
    <Text style={styles.eyebrow}>{sq ? "RRJETI I KARIKIMIT" : "CHARGING NETWORK"}</Text><Text style={styles.title}>{t.stations}</Text><Text style={styles.body}>{t.stationIntro}</Text>
    <ChoiceField label={sq ? "Qyteti" : "City"} value={city} options={[{ value: "", label: t.allCities }, ...cities.map(value => ({ value, label: value }))]} onChange={setCity} language={language}/>
    <ChoiceField label={sq ? "Fuqia e pikës" : "Charger rating"} value={power} options={[{ value: "all", label: sq ? "Çdo fuqi" : "Any rating" }, { value: "ac", label: "≤ 22 kW" }, { value: "fast", label: "> 22 kW" }]} onChange={value => setPower(value as typeof power)} language={language}/>
    <Chip title={`${availableOnly ? "✓ " : ""}${t.availableOnly}`} selected={availableOnly} onPress={() => setAvailableOnly(value => !value)}/>
    <Text style={styles.body}>{shown.length} {t.stations.toLowerCase()}</Text><Notice text={error}/>
    {stale && <Notice text={sq ? "Lidhja është ndërprerë. Gjendjet e pikave janë nga përditësimi i fundit." : "Connection lost. Station statuses are from the last update."}/>}
    {shown.length ? shown.map(station => {
      const status = stationState(station.status, language);
      return <Card key={station.id}>
        <View style={[styles.row, { flexWrap: "wrap" }]}><Text style={[styles.label, { fontSize: 17, flexShrink: 1 }]}>{station.name}</Text><Text style={[styles.label, { color: stale ? "#536b5e" : status.color }]}>{stale ? `${sq ? "Gjendja e fundit" : "Last status"}: ` : ""}{status.text}</Text></View>
        <Text style={styles.body}>{station.address} · {station.city}</Text>
        <Text style={styles.body}>{station.connectorCount} × {station.connectorType} · {station.powerKW} kW</Text>
        <Text style={styles.label}>{station.pricePerKWh.toFixed(2)} {station.currency} / kWh</Text>
        {station.isDemo && <Text style={styles.tag}>{t.demoStations}</Text>}
        <Button title={sq ? "Shiko pikën dhe karikimin" : "View station and charging"} onPress={() => onSelect(station.id)}/>
        <Button title={t.openInMaps} secondary onPress={() => void openMap(station)}/>
      </Card>;
    }) : <Card><Text style={styles.body}>{stations.length ? t.noStations : (sq ? "Ende nuk ka pika të publikuara. Provo përsëri më vonë." : "No stations have been published yet. Please try again later.")}</Text>{(city || availableOnly || power !== "all") && <Button title={sq ? "Pastro filtrat" : "Reset filters"} secondary onPress={() => { setCity(""); setPower("all"); setAvailableOnly(false); }}/>}</Card>}
  </Screen>;
}
