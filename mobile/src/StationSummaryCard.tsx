import { Pressable, StyleSheet, Text, View } from "react-native";
import type { Language, Station } from "../../contracts/account";
import { stationState } from "./station-data";
import { messages } from "./i18n";
import { Button, colors, styles } from "./ui";

export function StationSummaryCard({ station, language, stale, onOpen, onNavigate, onClose, actionsVisible = true, onToggleActions }: {
  station: Station; language: Language; stale: boolean; onOpen: () => void; onNavigate: () => void; onClose?: () => void;
  actionsVisible?: boolean; onToggleActions?: () => void;
}) {
  const sq = language === "sq", t = messages[language], status = stationState(station.status, language);
  const summary = <View style={layout.summary}>
    <View style={layout.heading}>
      <View accessible={false} style={layout.icon}><Text style={layout.bolt}>ϟ</Text></View>
      <View style={{ flex: 1, gap: 4 }}>
        <Text style={layout.eyebrow}>{sq ? "PIKA E KARIKIMIT" : "CHARGE POINT"}</Text>
        <Text accessibilityRole="header" style={layout.name}>{station.name}</Text>
      </View>
      {onToggleActions && <Text accessible={false} style={layout.chevron}>{actionsVisible ? "⌃" : "⌄"}</Text>}
      {onClose && <Pressable accessibilityRole="button" accessibilityLabel={sq ? "Mbyll detajet e pikës" : "Close station details"} onPress={onClose} style={layout.close}><Text style={{ fontSize: 24, color: colors.muted }}>×</Text></Pressable>}
    </View>
    <View style={layout.status}>
      <View style={[layout.dot, { backgroundColor: stale ? colors.muted : status.color }]}/>
      <Text style={{ color: stale ? colors.muted : status.color, fontWeight: "600", fontSize: 13, flexShrink: 1 }}>{stale ? `${sq ? "Gjendja e fundit" : "Last status"}: ` : ""}{status.text}</Text>
    </View>
    <Text style={styles.body}>{station.address} · {station.city}</Text>
    <View style={layout.specs}>
      <View style={layout.spec}><Text style={layout.caption}>{sq ? "Fuqia" : "Power"}</Text><Text style={layout.value}>{station.powerKW} <Text style={layout.unit}>kW</Text></Text></View>
      <View style={layout.spec}><Text style={layout.caption}>{sq ? "Lidhësit" : "Connectors"}</Text><Text style={layout.value}>{station.connectorCount} × {station.connectorType}</Text></View>
      <View style={layout.spec}><Text style={layout.caption}>{sq ? "Tarifa" : "Rate"}</Text><Text style={layout.value}>{new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(station.pricePerKWh)} <Text style={layout.unit}>{station.currency} / kWh</Text></Text></View>
    </View>
  </View>;
  return <View style={[layout.card, onToggleActions && actionsVisible && { borderColor: colors.green }]}>
    {onToggleActions ? <Pressable accessibilityRole="button" accessibilityState={{ expanded: actionsVisible }}
      accessibilityHint={actionsVisible ? (sq ? "Prek për të fshehur butonat" : "Tap to hide actions") : (sq ? "Prek për të shfaqur butonat" : "Tap to show actions")}
      onPress={onToggleActions} style={({ pressed }) => ({ opacity: pressed ? .7 : 1 })}>{summary}</Pressable> : summary}
    {actionsVisible && <View style={layout.actions}>
      <View style={layout.action}><Button title={sq ? "Hap karikimin" : "Open charging"} onPress={onOpen}/></View>
      <View style={layout.action}><Button title={t.openInMaps} secondary onPress={onNavigate}/></View>
    </View>}
    {station.isDemo && <Text style={layout.demo}>{t.demoStations} {t.verifyDemoLocation}</Text>}
  </View>;
}

const layout = StyleSheet.create({
  card: { backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border, borderRadius: 22, padding: 18, gap: 14 },
  summary: { gap: 14 },
  chevron: { color: colors.green, fontSize: 24, width: 24, textAlign: "center" },
  heading: { flexDirection: "row", alignItems: "center", gap: 12 },
  icon: { width: 44, height: 44, borderRadius: 14, backgroundColor: colors.ink, alignItems: "center", justifyContent: "center" },
  bolt: { fontSize: 29, fontWeight: "700", color: colors.lime },
  eyebrow: { fontSize: 10, letterSpacing: 1, fontWeight: "700", color: colors.muted },
  name: { fontSize: 20, fontWeight: "700", color: colors.ink },
  close: { minWidth: 44, minHeight: 44, alignItems: "center", justifyContent: "center", alignSelf: "flex-start" },
  status: { flexDirection: "row", alignItems: "center", alignSelf: "flex-start", gap: 7, backgroundColor: "#f0f5ed", borderRadius: 10, paddingHorizontal: 10, paddingVertical: 7, maxWidth: "100%" },
  dot: { width: 7, height: 7, borderRadius: 4 },
  specs: { flexDirection: "row", flexWrap: "wrap", gap: 14, borderTopWidth: 1, borderTopColor: colors.border, paddingTop: 14 },
  spec: { minWidth: 95, flexGrow: 1, gap: 4 },
  caption: { fontSize: 12, color: colors.muted },
  value: { fontSize: 17, fontWeight: "600", color: colors.ink },
  unit: { fontSize: 12, fontWeight: "400" },
  actions: { flexDirection: "row", flexWrap: "wrap", gap: 10 },
  action: { minWidth: 120, flexGrow: 1, flexBasis: "44%" },
  demo: { fontSize: 12, lineHeight: 18, color: colors.muted },
});
