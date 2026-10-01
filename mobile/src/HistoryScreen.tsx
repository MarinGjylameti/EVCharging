import { useState } from "react";
import { Pressable, RefreshControl, ScrollView, Text, View } from "react-native";
import type { ChargingHistory, Language } from "../../contracts/account";
import { messages } from "./i18n";
import { Card, colors, styles } from "./ui";

export function HistoryScreen({history, language, refresh}: {history:ChargingHistory[];language:Language;refresh:()=>Promise<void>}) {
  const t=messages[language];
  const [selectedId,setSelectedId]=useState<string|null>(null);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState("");
  const selected=history.find(x=>x.id===selectedId) ?? history[0];
  const totalEnergy=history.reduce((sum,x)=>sum+x.energyKWh,0);
  const totalCost=history.reduce((sum,x)=>sum+x.totalCost,0);
  const date=(value:string)=>new Intl.DateTimeFormat(language==="sq"?"sq-AL":"en-GB",{dateStyle:"medium",timeStyle:"short"}).format(new Date(value));
  async function reload(){setBusy(true);setError("");try{await refresh();}catch{setError(t.service_unavailable);}finally{setBusy(false);}}
  return <ScrollView contentContainerStyle={styles.content} refreshControl={<RefreshControl refreshing={busy} onRefresh={()=>void reload()}/> }>
    <Text style={styles.eyebrow}>{t.myActivity.toUpperCase()}</Text><Text style={styles.title}>{t.history}</Text>
    <Text style={styles.body}>{t.historyIntro}</Text>
    {history.some(x=>x.isDemo)&&<Text style={styles.notice}>{t.demoHistory}</Text>}
    <View style={{flexDirection:"row",gap:9}}><View style={[styles.card,{flex:1,padding:13}]}><Text style={styles.body}>{t.charges}</Text><Text style={[styles.title,{fontSize:24}]}>{history.length}</Text></View>
      <View style={[styles.card,{flex:1,padding:13}]}><Text style={styles.body}>{t.energy}</Text><Text style={[styles.title,{fontSize:20}]}>{totalEnergy.toFixed(1)} kWh</Text></View></View>
    <Card><Text style={styles.body}>{t.calculatedCost}</Text><Text style={[styles.title,{fontSize:24}]}>€{totalCost.toFixed(2)}</Text></Card>
    {error?<Text style={styles.notice}>{error}</Text>:null}
    {history.length ? history.map(item=><Pressable accessibilityRole="button" accessibilityState={{selected:selected?.id===item.id}} key={item.id} onPress={()=>setSelectedId(item.id)}>
      <Card><View style={styles.row}><View style={{flex:1}}><Text style={[styles.label,{fontSize:15}]}>{item.stationName}</Text><Text style={styles.body}>{item.city} · {date(item.startedAt)}</Text></View>
        <Text style={[styles.label,{color:colors.green}]}>{item.energyKWh.toFixed(1)} kWh</Text></View>
        {selected?.id===item.id&&<View style={{gap:8,borderTopWidth:1,borderTopColor:colors.border,paddingTop:12}}>
          <Text style={styles.body}>{t.started}: {date(item.startedAt)}</Text><Text style={styles.body}>{t.finished}: {date(item.endedAt)}</Text>
          <Text style={styles.body}>{t.pricePerKWh}: €{item.unitPriceSnapshot.toFixed(2)}</Text>
          <Text style={styles.label}>{t.calculatedCost}: €{item.totalCost.toFixed(2)}</Text>
          <Text style={[styles.body,{fontSize:12}]}>{t.noPayment}</Text></View>}
      </Card></Pressable>) : <Card><Text style={styles.body}>{t.noHistory}</Text></Card>}
  </ScrollView>;
}
