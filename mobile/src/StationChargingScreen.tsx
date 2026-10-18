import { useEffect, useRef, useState } from "react";
import { ActivityIndicator, Text, View } from "react-native";
import type { Language, StationCharging } from "../../contracts/account";
import { request } from "./api";
import { isStationId } from "./config";
import { messages, errorText } from "./i18n";
import { chargingDuration, chargingValues, ownChargingSession, stationState } from "./station-data";
import { useForegroundPoll } from "./use-foreground-poll";
import { Button, Card, Chip, Notice, Screen, colors, styles } from "./ui";
import { chargingGuidance } from "../../shared/charging-guidance";

const sessionLabels: Record<string, [string, string]> = {
  Charging: ["Në karikim", "Charging"], Connecting: ["Makina po lidhet", "Connecting vehicle"],
  PausedByVehicle: ["Në pauzë nga makina", "Paused by vehicle"], PausedByCharger: ["Në pauzë nga pika", "Paused by charger"],
  Finishing: ["Duke përfunduar", "Finishing"], Completed: ["Karikimi përfundoi", "Charge completed"],
  ConnectionLost: ["Lidhja u ndërpre", "Connection lost"], Faulted: ["Pika raportoi defekt", "Charger reported a fault"],
};
export function StationChargingScreen({ stationId, token, language, onBack }: { stationId: string; token: string; language: Language; onBack: () => void }) {
  const sq = language === "sq", t = messages[language];
  const [snapshot, setSnapshot] = useState<StationCharging | null>(null);
  const [connector, setConnector] = useState(1);
  const [error, setError] = useState("");
  const [stale, setStale] = useState(true);
  const [busy, setBusy] = useState(false);
  const [now, setNow] = useState(Date.now());
  const mounted = useRef(true), mutation = useRef(0), readRevision = useRef(0), busyRef = useRef(false);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  useForegroundPoll(async () => { setNow(Date.now()); }, 1000, !!snapshot?.connectionRequest || !!snapshot?.session && !snapshot.session.stoppedAt, `${stationId}:${connector}`);
  async function refresh(signal?: AbortSignal) {
    if (!isStationId(stationId)) { setError("configuration"); return; }
    const epoch = mutation.current;
    const revision = ++readRevision.current;
    try {
      const value = await request<StationCharging>(`/driver/stations/${stationId}/charging?connector=${connector}`, token, "GET", undefined, signal);
      if (mounted.current && !signal?.aborted && epoch === mutation.current && revision === readRevision.current) { setSnapshot(value); setStale(false); setError(""); }
    } catch (failure) {
      if (mounted.current && !signal?.aborted && epoch === mutation.current && revision === readRevision.current) { setStale(true); setError(failure instanceof Error ? failure.message : "genericError"); }
      if (!signal?.aborted) throw failure;
    }
  }
  useForegroundPoll(refresh, 5000, true, `${stationId}:${connector}`);
  async function connect(cancel: boolean) {
    if (busyRef.current || !snapshot?.canConnect || connector !== 1) return;
    busyRef.current = true; mutation.current++; setBusy(true); setError("");
    try {
      const value = await request<StationCharging>(`/driver/stations/${stationId}/${cancel ? "cancel" : "connect"}`, token, "POST");
      mutation.current++;
      if (mounted.current) { setSnapshot(value); setStale(false); }
    } catch (failure) {
      mutation.current++;
      if (mounted.current) setError(failure instanceof Error ? failure.message : "genericError");
    } finally { busyRef.current = false; if (mounted.current) setBusy(false); }
  }
  const number = (value: number | null) => value === null ? "—" : new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { maximumFractionDigits: 3 }).format(value);
  const session = snapshot ? ownChargingSession(snapshot) : null;
  const values = session ? chargingValues(session, now, stale) : null;
  const waiting = snapshot?.connectionRequest;
  const waitingExpired = waiting && now >= Date.parse(waiting.expiresAt);
  const guidance = snapshot ? chargingGuidance(snapshot, language, stale) : null;
  return <Screen language={language} refresh={() => refresh()}>
    <Button title={sq ? "← Kthehu te pikat" : "← Back to stations"} secondary onPress={onBack}/>
    <Notice text={error ? errorText(new Error(error), language) : ""}/>
    {!snapshot ? <Card>{!error && <ActivityIndicator color={colors.green}/>}<Text style={styles.body}>{error ? (sq ? "Pika nuk u ngarkua. Provo përsëri ose kthehu te lista." : "The station could not be loaded. Try again or return to the list.") : t.loading}</Text><Button title={t.retry} secondary onPress={() => void refresh().catch(() => {})}/></Card> : <>
      <Text style={styles.title}>{snapshot.station.name}</Text><Text style={styles.body}>{snapshot.station.address} · {snapshot.station.city}</Text>
      <Text style={styles.tag}>{stale ? (sq ? "Në pritje të përditësimit" : "Waiting for an update") : (sq ? "Përditësohet çdo 5 sekonda" : "Updates every 5 seconds")}</Text>
      <Card><Text style={styles.label}>{stationState(snapshot.station.status, language).text}</Text><Text style={styles.body}>{snapshot.station.connectorType} · {snapshot.station.powerKW} kW</Text><Text style={styles.body}>{session?.unitPricePerKWh ?? snapshot.station.pricePerKWh} {session?.currency ?? snapshot.station.currency} / kWh</Text>{snapshot.station.isDemo && <Text style={styles.tag}>{t.demoHistory}</Text>}</Card>
      {snapshot.station.connectorCount > 1 && <View style={styles.wrap}>{Array.from({ length: snapshot.station.connectorCount }, (_, index) => index + 1).map(value => <Chip key={value} disabled={busy} title={`${sq ? "Lidhësi" : "Connector"} ${value}`} selected={connector === value} onPress={() => { mutation.current++; setConnector(value); setSnapshot(null); setStale(true); }}/>)}</View>}
      {session && values ? <Card>
        <Text style={styles.eyebrow}>{sq ? "KARIKIMI YT" : "YOUR CHARGE"}</Text>
        <Text style={[styles.label, { fontSize: 19 }]}>{session.stoppedAt ? (sq ? "Karikimi përfundoi" : "Charge completed") : stale ? (sq ? "Në pritje të përditësimit" : "Waiting for an update") : sessionLabels[session.status]?.[sq ? 0 : 1] ?? (sq ? "Në pritje të statusit" : "Awaiting status")}</Text>
        <View style={[styles.row, { flexWrap: "wrap" }]}><Text style={styles.body}>{sq ? "Kohëzgjatja" : "Duration"}</Text><Text style={styles.metric}>{chargingDuration(session, now)}</Text></View>
        <Text style={styles.body}>{t.started}: {new Date(session.isDemo ? session.startReceivedAt ?? session.startedAt : session.startedAt).toLocaleString(sq ? "sq-AL" : "en-GB", { timeZone: "Europe/Tirane" })}</Text>
        <View style={[styles.row, { flexWrap: "wrap" }]}><Text style={styles.body}>{t.energy}</Text><Text style={styles.metric}>{number(values.energy)} kWh</Text></View>
        <View style={[styles.row, { flexWrap: "wrap" }]}><Text style={styles.body}>{sq ? "Fuqia e matur" : "Measured power"}</Text><Text style={styles.metric}>{number(values.power)} kW</Text></View>
        <View style={[styles.row, { flexWrap: "wrap" }]}><Text style={styles.body}>{sq ? "Bateria" : "Battery"}</Text><Text style={styles.metric}>{number(values.battery)}%</Text></View>
        <View style={[styles.row, { flexWrap: "wrap" }]}><Text style={styles.body}>{t.calculatedCost}</Text><Text style={styles.label}>{number(values.cost)} {session.currency}</Text></View>
        {session.completionWarning && <Notice text={sq ? "Matja përfundimtare kërkon verifikim. Kostoja finale nuk është konfirmuar." : "The final reading needs verification. Final cost has not been confirmed."}/>}
        {session.faultCode && <Notice text={`${sq ? "Pika raportoi defekt" : "Charger reported a fault"}: ${session.faultCode}`}/>}
        <Text style={styles.body}>{t.noPayment}</Text>
      </Card> : guidance && <Card><Text accessibilityRole="header" style={[styles.label, { fontSize: 18 }]}>{snapshot.canConnect && waiting ? (waitingExpired ? (sq ? "Koha e pritjes përfundoi" : "Waiting time expired") : (sq ? "Në pritje të nisjes nga karikuesi" : "Waiting for the charger to start")) : guidance.title}</Text><Text style={styles.body}>{snapshot.canConnect ? (sq ? "Seanca shfaqet kur pajisja konfirmon nisjen e karikimit." : "Your session appears when the charger confirms the start.") : guidance.body}</Text>{!snapshot.canConnect && guidance.steps.map((step, index) => <View key={step} style={[styles.row, { justifyContent: "flex-start", alignItems: "flex-start" }]}><Text style={styles.tag}>{index + 1}</Text><Text style={[styles.body, { flex: 1 }]}>{step}</Text></View>)}</Card>}
      {snapshot.canConnect && connector === 1 && !snapshot.inUse && (!session || session.stoppedAt) && <Button title={busy ? t.working : waiting && !waitingExpired ? (sq ? "Anulo pritjen" : "Cancel waiting") : (sq ? "Përgatit lidhjen me pikën" : "Prepare charger connection")} disabled={busy || stale} onPress={() => void connect(!!waiting && !waitingExpired)}/>}
    </>}
  </Screen>;
}
