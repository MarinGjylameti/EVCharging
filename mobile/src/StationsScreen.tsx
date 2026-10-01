import { useMemo, useState } from "react";
import { Linking, Platform, Pressable, RefreshControl, ScrollView, Text, View } from "react-native";
import type { Language, Station } from "../../contracts/account";
import { messages } from "./i18n";
import { Button, Card, colors, styles } from "./ui";

const statusColor: Record<Station["status"], string> = {
  Available:"#287d57", Occupied:"#bc7a21", Charging:"#3b79bd",
  OutOfService:"#b75551", Maintenance:"#728073"
};
const statusName: Record<Station["status"],{sq:string;en:string}> = {
  Available:{sq:"E lirë",en:"Available"}, Occupied:{sq:"E zënë",en:"Occupied"},
  Charging:{sq:"Në karikim",en:"Charging"}, OutOfService:{sq:"Jashtë funksionit",en:"Out of service"},
  Maintenance:{sq:"Në mirëmbajtje",en:"Maintenance"}
};

export function StationsScreen({stations, language, refresh}: {stations:Station[];language:Language;refresh:()=>Promise<void>}) {
  const t=messages[language];
  const [city,setCity]=useState<string|null>(null);
  const [availableOnly,setAvailableOnly]=useState(false);
  const [selectedId,setSelectedId]=useState<string|null>(null);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState("");
  const cities=useMemo(()=>[...new Set(stations.map(x=>x.city))], [stations]);
  const shown=stations.filter(x=>(city===null||x.city===city)&&(!availableOnly||x.status==="Available"));
  const selected=shown.find(x=>x.id===selectedId) ?? shown[0];
  async function reload() { setBusy(true); setError(""); try { await refresh(); } catch { setError(t.service_unavailable); } finally { setBusy(false); } }
  async function openMap(station: Station) {
    const lat=station.latitude.toFixed(6), lon=station.longitude.toFixed(6);
    const url=Platform.OS==="ios" ? `https://maps.apple.com/?ll=${lat},${lon}&q=${encodeURIComponent(station.name)}` :
      `geo:${lat},${lon}?q=${lat},${lon}(${encodeURIComponent(station.name)})`;
    try { await Linking.openURL(url); } catch { setError(t.mapUnavailable); }
  }
  return <ScrollView contentContainerStyle={styles.content} refreshControl={<RefreshControl refreshing={busy} onRefresh={()=>void reload()}/> }>
    <Text style={styles.eyebrow}>{t.networkTitle.toUpperCase()}</Text>
    <Text style={styles.title}>{t.stations}</Text>
    <Text style={styles.body}>{t.stationIntro}</Text>
    {stations.some(x=>x.isDemo)&&<Text style={styles.notice}>{t.demoStations}</Text>}
    <View style={styles.row}><Text style={styles.label}>{stations.length} {t.stations.toLowerCase()}</Text>
      <Pressable accessibilityRole="button" onPress={()=>void reload()} disabled={busy}><Text style={{color:colors.green,fontWeight:"700"}}>{t.refresh}</Text></Pressable></View>
    <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{gap:8,paddingVertical:4}}>
      <Pressable accessibilityRole="button" accessibilityState={{selected:city===null}} onPress={()=>{setCity(null);setSelectedId(null);}} style={[styles.secondary,{padding:10,borderRadius:9},city===null&&{backgroundColor:colors.lime}]}><Text style={styles.label}>{t.allCities}</Text></Pressable>
      {cities.map(value=><Pressable key={value} accessibilityRole="button" accessibilityState={{selected:city===value}} onPress={()=>{setCity(value);setSelectedId(null);}} style={[styles.secondary,{padding:10,borderRadius:9},city===value&&{backgroundColor:colors.lime}]}><Text style={styles.label}>{value}</Text></Pressable>)}
    </ScrollView>
    <Pressable accessibilityRole="button" accessibilityState={{selected:availableOnly}} onPress={()=>{setAvailableOnly(!availableOnly);setSelectedId(null);}}><Text style={[styles.tag,{backgroundColor:availableOnly?colors.lime:"#edf3e6"}]}>{availableOnly?"✓ ":""}{t.availableOnly}</Text></Pressable>
    {error?<Text style={styles.notice}>{error}</Text>:null}
    {shown.length ? shown.map(station=><Pressable key={station.id} accessibilityRole="button" accessibilityState={{selected:selected?.id===station.id}} onPress={()=>setSelectedId(station.id)}>
      <Card><View style={styles.row}><View style={{flex:1}}><Text style={[styles.label,{fontSize:16}]}>{station.name}</Text><Text style={styles.body}>{station.city} · {station.code}</Text></View>
        <Text style={{color:statusColor[station.status],fontSize:11,fontWeight:"700"}}>{statusName[station.status][language]}</Text></View>
        <Text style={styles.body}>{station.connectorCount} × {station.connectorType} · {station.powerKW} kW</Text>
        <Text style={styles.label}>€{station.pricePerKWh.toFixed(2)} / kWh</Text>
        {selected?.id===station.id&&<View style={{gap:10,borderTopWidth:1,borderTopColor:colors.border,paddingTop:14}}>
          <Text style={styles.body}>{station.address}</Text>
          {station.isDemo&&<Text style={[styles.body,{fontSize:12}]}>{t.verifyDemoLocation}</Text>}
          <Button title={t.openInMaps} secondary onPress={()=>void openMap(station)}/>
        </View>}
      </Card></Pressable>) : <Card><Text style={styles.body}>{t.noStations}</Text></Card>}
  </ScrollView>;
}
