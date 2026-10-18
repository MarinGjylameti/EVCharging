import { Pressable, StyleSheet, Text, View } from "react-native";
import type { Language } from "../../contracts/account";
import { messages } from "./i18n";
import { colors } from "./ui";

export type BottomTab = "stations" | "history" | "profile";

function TabIcon({ tab, color }: { tab: BottomTab; color: string }) {
  return <View accessible={false} accessibilityElementsHidden importantForAccessibility="no-hide-descendants" style={layout.icon}>
    {tab === "stations" ? <>
      <View style={[layout.pin, { borderColor: color }]}/><View style={[layout.pinDot, { backgroundColor: color }]}/>
    </> : tab === "history" ? <>
      <View style={[layout.clock, { borderColor: color }]}/><View style={[layout.clockMinute, { backgroundColor: color }]}/><View style={[layout.clockHour, { backgroundColor: color }]}/>
    </> : <>
      <View style={[layout.personHead, { borderColor: color }]}/><View style={[layout.personBody, { borderColor: color }]}/>
    </>}
  </View>;
}

export function BottomNavigation({ active, language, onSelect }: { active: BottomTab; language: Language; onSelect: (tab: BottomTab) => void }) {
  const t = messages[language];
  return <View style={layout.container}><View style={layout.bar}>
    {(["stations", "history", "profile"] as const).map(tab => {
      const selected = active === tab, label = tab === "profile" ? t.overview : t[tab];
      return <Pressable key={tab} accessibilityRole="tab" accessibilityLabel={label} accessibilityState={{ selected }} onPress={() => onSelect(tab)}
        style={({ pressed }) => [layout.tab, selected && layout.selected, pressed && { opacity: .7 }]}>
        <TabIcon tab={tab} color={selected ? colors.lime : colors.muted}/>
        <Text style={[layout.label, selected && layout.selectedLabel]}>{label}</Text>
      </Pressable>;
    })}
  </View></View>;
}

const layout = StyleSheet.create({
  container: { paddingHorizontal: 16, paddingTop: 6, paddingBottom: 6 },
  bar: { flexDirection: "row", gap: 4, padding: 5, borderRadius: 26, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.white,
    shadowColor: colors.ink, shadowOpacity: .1, shadowRadius: 12, shadowOffset: { width: 0, height: 3 }, elevation: 4 },
  tab: { flex: 1, minHeight: 58, paddingVertical: 8, paddingHorizontal: 4, borderRadius: 20, alignItems: "center", justifyContent: "center", gap: 5 },
  selected: { backgroundColor: colors.ink },
  label: { fontSize: 12, lineHeight: 16, fontWeight: "600", color: colors.muted, textAlign: "center" },
  selectedLabel: { color: colors.white, fontWeight: "700" },
  icon: { width: 24, height: 24 },
  pin: { position: "absolute", left: 4, top: 2, width: 16, height: 16, borderWidth: 2, borderTopLeftRadius: 8, borderTopRightRadius: 8, borderBottomLeftRadius: 8, borderBottomRightRadius: 2, transform: [{ rotate: "45deg" }] },
  pinDot: { position: "absolute", left: 10, top: 8, width: 4, height: 4, borderRadius: 2 },
  clock: { position: "absolute", left: 2, top: 2, width: 20, height: 20, borderWidth: 2, borderRadius: 10 },
  clockMinute: { position: "absolute", left: 11, top: 6, width: 2, height: 7, borderRadius: 1 },
  clockHour: { position: "absolute", left: 11, top: 11, width: 6, height: 2, borderRadius: 1 },
  personHead: { position: "absolute", left: 8, top: 2, width: 8, height: 8, borderWidth: 2, borderRadius: 4 },
  personBody: { position: "absolute", left: 3, top: 13, width: 18, height: 9, borderWidth: 2, borderTopLeftRadius: 9, borderTopRightRadius: 9, borderBottomLeftRadius: 3, borderBottomRightRadius: 3 },
});
