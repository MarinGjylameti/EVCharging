import { StyleSheet, Text, View } from "react-native";
import type { DriverChargingSession, Language } from "../../contracts/account";
import { chargingEstimate, formatChargingEstimate, chargingEstimateNotes } from "../../shared/charging-estimate";
import { chargingDuration, chargingValues } from "./station-data";
import { messages } from "./i18n";
import { Button, Notice, colors, styles } from "./ui";

const sessionLabels: Record<string, [string, string]> = {
  Charging: ["Në karikim", "Charging"], Connecting: ["Makina po lidhet", "Connecting vehicle"],
  PausedByVehicle: ["Në pauzë nga makina", "Paused by vehicle"], PausedByCharger: ["Në pauzë nga pika", "Paused by charger"],
  Finishing: ["Duke përfunduar", "Finishing"], Completed: ["Karikimi përfundoi", "Charge completed"],
  ConnectionLost: ["Lidhja u ndërpre", "Connection lost"], Faulted: ["Pika raportoi defekt", "Charger reported a fault"],
  Unavailable: ["Pika është jashtë shërbimit", "Charger unavailable"],
};

export function ChargingProgressCard({ session, language, now, stale, onOpenProfile }: {
  session: DriverChargingSession; language: Language; now: number; stale: boolean; onOpenProfile: () => void;
}) {
  const sq = language === "sq", t = messages[language];
  const ended = session.stoppedAt !== null;
  const values = chargingValues(session, now, stale);
  const battery = ended && session.stateOfCharge !== null && Number.isFinite(session.stateOfCharge) && session.stateOfCharge >= 0 && session.stateOfCharge <= 100 ? session.stateOfCharge : values.battery;
  const estimate = chargingEstimate(session, now, stale);
  const faulted = !ended && (session.status === "Faulted" || session.status === "Unavailable" || !!session.faultCode && session.faultCode !== "NoError");
  const paused = !ended && (session.status === "PausedByVehicle" || session.status === "PausedByCharger");
  const charging = !ended && !stale && !faulted && session.status === "Charging";
  const state = ended ? sessionLabels.Completed[sq ? 0 : 1] : stale ? (sq ? "Në pritje të përditësimit" : "Waiting for an update")
    : faulted ? sessionLabels.Faulted[sq ? 0 : 1] : sessionLabels[session.status]?.[sq ? 0 : 1] ?? (sq ? "Në pritje të statusit" : "Awaiting status");
  const number = (value: number | null, digits = 2) => value === null || !Number.isFinite(value) ? "—"
    : new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { maximumFractionDigits: digits }).format(value);
  const batteryNote = ended ? (battery === null ? (sq ? "Pajisja nuk raportoi nivelin e baterisë" : "No battery level was reported") : (sq ? "Matja e fundit e baterisë" : "Last reported battery level")) : battery !== null ? (sq ? "Niveli i raportuar nga pajisja" : "Battery level reported by the charger")
    : stale ? chargingEstimateNotes.stale[sq ? 0 : 1] : session.stateOfCharge == null ? chargingEstimateNotes.missingBattery[sq ? 0 : 1]
    : chargingEstimateNotes.staleBattery[sq ? 0 : 1];
  const start = new Date(session.isDemo ? session.startReceivedAt ?? session.startedAt : session.startedAt);
  const started = Number.isFinite(start.getTime()) ? start.toLocaleString(sq ? "sq-AL" : "en-GB", {
    timeZone: "Europe/Tirane", day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23",
  }) : "—";
  const metrics = [
    { label: t.energy, value: number(values.energy, 3), unit: "kWh" },
    { label: sq ? "Fuqia aktuale" : "Current power", value: number(values.power, 3), unit: "kW" },
    { label: t.calculatedCost, value: number(session.completionWarning ? null : values.cost), unit: session.currency },
    { label: sq ? "Kohëzgjatja" : "Duration", value: chargingDuration(session, now), unit: "" },
  ];

  return <View style={layout.card}>
    <View style={layout.banner}>
      <Text style={layout.eyebrow}>{sq ? "KARIKIMI YT" : "YOUR CHARGE"}</Text>
      <View style={[layout.status, { backgroundColor: stale ? "#e8eeeb" : faulted ? "#ffe2db" : paused ? "#ffedc2" : charging || ended ? "#d5edac" : "#e8eeeb" }]}>
        <Text style={[layout.statusText, { color: faulted && !stale ? "#8a3025" : paused && !stale ? "#654716" : colors.ink }]}>{state}</Text>
      </View>
      <View style={layout.batteryHeading}>
        <View style={{ flex: 1, minWidth: 100, gap: 5 }}>
          <Text style={layout.bannerLabel}>{sq ? "Niveli i baterisë" : "Battery level"}</Text>
          <Text style={layout.percentage}>{number(battery, 1)}<Text style={layout.percentUnit}>{battery !== null ? " %" : ""}</Text></Text>
        </View>
        <View accessible={false} accessibilityElementsHidden importantForAccessibility="no-hide-descendants" style={layout.batteryIcon}>
          <View style={layout.batteryOutline}><View style={[layout.batteryFill, { width: `${battery ?? 0}%`, backgroundColor: charging ? colors.lime : "#a8bcb1" }]}/></View>
          <View style={layout.batteryTip}/>
        </View>
      </View>
      <View accessible accessibilityRole="progressbar" accessibilityLabel={sq ? "Niveli i baterisë" : "Battery level"}
        accessibilityValue={battery === null ? { text: batteryNote } : { min: 0, max: 100, now: battery, text: `${number(battery, 1)}%` }} style={layout.track}>
        <View style={[layout.fill, { width: `${battery ?? 0}%`, backgroundColor: charging ? colors.lime : "#a8bcb1" }]}/>
      </View>
      <Text style={layout.bannerNote}>{batteryNote}</Text>
    </View>
    <View style={layout.details}>
      {!ended && <View style={layout.estimate}>
        <Text style={layout.label}>{sq ? "Koha deri në bateri të plotë" : "Time until battery is full"}</Text>
        <Text style={layout.estimateValue}>{estimate.minutes === null ? "—" : `≈ ${formatChargingEstimate(estimate.minutes)}`}</Text>
        <Text style={styles.body}>{estimate.reason ? chargingEstimateNotes[estimate.reason][sq ? 0 : 1]
          : (sq ? "Deri në 100%, me fuqinë aktuale. Koha mund të zgjatet nëse fuqia ulet." : "To 100% at current power. May take longer if power drops.")}</Text>
        {estimate.reason === "missingCapacity" && <Button title={sq ? "Plotëso profilin" : "Update profile"} secondary onPress={onOpenProfile}/>}
      </View>}
      <View style={layout.metrics}>{metrics.map(metric => <View key={metric.label} style={layout.metric}>
        <Text style={layout.label}>{metric.label}</Text>
        <Text style={layout.metricValue}>{metric.value}{metric.unit ? <Text style={layout.unit}> {metric.unit}</Text> : null}</Text>
      </View>)}</View>
      <Text style={styles.body}>{t.started}: {started}</Text>
      {session.completionWarning && <Notice text={sq ? "Matja përfundimtare kërkon verifikim. Kostoja finale nuk është konfirmuar." : "The final reading needs verification. Final cost has not been confirmed."}/>}
      {session.faultCode && session.faultCode !== "NoError" && <Notice text={`${sq ? "Pika raportoi defekt" : "Charger reported a fault"}: ${session.faultCode}`}/>}
      <Text style={styles.body}>{t.noPayment}</Text>
    </View>
  </View>;
}

const layout = StyleSheet.create({
  card: { borderRadius: 22, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.white, overflow: "hidden" },
  banner: { backgroundColor: colors.ink, padding: 22, gap: 14 },
  eyebrow: { color: "#c2d8cb", fontSize: 11, fontWeight: "700", letterSpacing: 1.7 },
  status: { alignSelf: "flex-start", borderRadius: 12, paddingHorizontal: 12, paddingVertical: 7, maxWidth: "100%" },
  statusText: { fontSize: 13, fontWeight: "600", flexShrink: 1 },
  batteryHeading: { flexDirection: "row", flexWrap: "wrap", alignItems: "center", gap: 16, paddingVertical: 4 },
  bannerLabel: { color: "#dce9e1", fontSize: 14 },
  percentage: { color: colors.white, fontSize: 54, fontWeight: "700", letterSpacing: -2, fontVariant: ["tabular-nums"] },
  percentUnit: { fontSize: 24, fontWeight: "400", color: "#dce9e1", letterSpacing: 0 },
  batteryIcon: { flexDirection: "row", alignItems: "center" },
  batteryOutline: { width: 66, height: 34, padding: 4, borderWidth: 2, borderColor: "#9ebbaa", borderRadius: 8 },
  batteryFill: { height: "100%", borderRadius: 3 },
  batteryTip: { width: 4, height: 14, backgroundColor: "#9ebbaa", borderTopRightRadius: 3, borderBottomRightRadius: 3 },
  track: { height: 12, borderRadius: 6, backgroundColor: "#3d5c4d", overflow: "hidden" },
  fill: { height: "100%", borderRadius: 6 },
  bannerNote: { color: "#c2d8cb", fontSize: 12, lineHeight: 18 },
  details: { padding: 20, gap: 18 },
  estimate: { gap: 7, paddingBottom: 18, borderBottomWidth: 1, borderBottomColor: colors.border },
  label: { fontSize: 13, lineHeight: 19, color: colors.muted },
  estimateValue: { fontSize: 30, fontWeight: "700", color: colors.ink, fontVariant: ["tabular-nums"] },
  metrics: { flexDirection: "row", flexWrap: "wrap", gap: 18 },
  metric: { flexBasis: "43%", flexGrow: 1, minWidth: 108, gap: 6 },
  metricValue: { fontSize: 22, color: colors.ink, fontWeight: "600", fontVariant: ["tabular-nums"] },
  unit: { fontSize: 13, fontWeight: "400", color: colors.muted },
});
