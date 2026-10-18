import { Pressable, StyleSheet, Text, View } from "react-native";
import type { DriverChargingSession, Language } from "../../contracts/account";
import { colors } from "./ui";

export function ActiveChargingShortcut({ sessions = [], language, currentStationId, currentConnector, stale, onOpen }: {
  sessions: DriverChargingSession[]; language: Language; currentStationId: string | null; currentConnector: number;
  stale: boolean; onOpen: (session: DriverChargingSession) => void;
}) {
  const sq = language === "sq";
  const active = sessions.filter(session => !stale && session.stoppedAt === null && session.status === "Charging"
    && !(session.stationId === currentStationId && session.connectorId === currentConnector));
  if (!active.length) return null;
  return <View style={layout.container}>{active.map(session => <Pressable key={session.id} accessibilityRole="button"
    accessibilityLabel={`${sq ? "Shko te karikimi" : "Return to charging"}: ${session.stationName}, ${sq ? "lidhësi" : "connector"} ${session.connectorId}`}
    onPress={() => onOpen(session)} style={({ pressed }) => [layout.button, pressed && { opacity: .8 }]}>
    <View accessible={false} style={layout.icon}><Text style={{ fontSize: 26, color: colors.ink }}>ϟ</Text></View>
    <View style={{ flex: 1, gap: 3 }}>
      <Text style={layout.title}>{sq ? "Shko te karikimi" : "Return to charging"}</Text>
      <Text style={layout.subtitle}>{session.stationName} · {sq ? "Lidhësi" : "Connector"} {session.connectorId}</Text>
    </View>
    <Text accessible={false} style={layout.arrow}>→</Text>
  </Pressable>)}</View>;
}

const layout = StyleSheet.create({
  container: { paddingHorizontal: 16, paddingVertical: 8, gap: 8 },
  button: { flexDirection: "row", alignItems: "center", gap: 12, minHeight: 64, backgroundColor: colors.ink, padding: 12, borderRadius: 17 },
  icon: { width: 36, height: 36, alignItems: "center", justifyContent: "center", backgroundColor: colors.lime, borderRadius: 12 },
  title: { color: colors.white, fontSize: 14, fontWeight: "700" },
  subtitle: { color: "#c2d8cb", fontSize: 12, lineHeight: 17 },
  arrow: { fontSize: 24, color: colors.lime },
});
