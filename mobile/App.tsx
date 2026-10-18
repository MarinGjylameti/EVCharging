import { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, BackHandler, Pressable, StatusBar, Text, View } from "react-native";
import { SafeAreaProvider, SafeAreaView } from "react-native-safe-area-context";
import * as Notifications from "expo-notifications";
import type { AuthResult, Language, User } from "../contracts/account";
import { readSession, saveSession, clearSession, request, subscribeSessionExpiry } from "./src/api";
import { createDriverLoader, type DriverData } from "./src/driver-data";
import { useForegroundPoll } from "./src/use-foreground-poll";
import { messages, errorText } from "./src/i18n";
import { AuthScreen } from "./src/AuthScreen";
import { ProfileScreen } from "./src/ProfileScreen";
import { SettingsScreen } from "./src/SettingsScreen";
import { StationsScreen } from "./src/StationsScreen";
import { StationChargingScreen } from "./src/StationChargingScreen";
import { ActiveChargingShortcut } from "./src/ActiveChargingShortcut";
import { HistoryScreen } from "./src/HistoryScreen";
import { NotificationsScreen } from "./src/NotificationsScreen";
import { registerPush, pushAvailable } from "./src/push";
import { waitForMobileApi } from "./src/wait-for-api";
import { Brand, Button, Notice, Screen, styles, colors } from "./src/ui";

type Tab = "stations" | "history" | "profile" | "notifications" | "settings";
function AppContent() {
  const [language, setLanguage] = useState<Language>("sq");
  const [user, setUser] = useState<User | null>(null);
  const userRef = useRef<User | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const tokenRef = useRef<string | null>(null);
  const [expiresAt, setExpiresAt] = useState("");
  const [tab, setTab] = useState<Tab>("stations");
  const [stationId, setStationId] = useState<string | null>(null);
  const [chargingConnector, setChargingConnector] = useState(1);
  const [loading, setLoading] = useState(true);
  const [warming, setWarming] = useState(false);
  const bootController = useRef<AbortController | null>(null);
  const [error, setError] = useState("");
  const [data, setData] = useState<Omit<DriverData, "user">>({ notifications: [], stations: [], history: [], sessions: [] });
  const [dataReady, setDataReady] = useState(false), [networkStale, setNetworkStale] = useState(false);
  const t = messages[language];
  const updateUser = useCallback((value: User) => {
    if (userRef.current?.id === value.id && userRef.current.version > value.version) return;
    userRef.current = value; setUser(value); setLanguage(value.language);
  }, []);
  const loaderRef = useRef<ReturnType<typeof createDriverLoader> | null>(null);
  if (!loaderRef.current) loaderRef.current = createDriverLoader(snapshot => {
    updateUser(snapshot.user); setData(snapshot); setDataReady(true); setNetworkStale(false); setError("");
  });
  const sessionEnded = useCallback((id: string) => {
    loaderRef.current!.confirmSessionEnded(id);
    setData(current => current.sessions.some(session => session.id === id) ? { ...current, sessions: current.sessions.filter(session => session.id !== id) } : current);
  }, []);
  const expire = useCallback(async (reason = "") => {
    tokenRef.current = null; userRef.current = null; loaderRef.current!.setSession(null);
    setToken(null); setUser(null); setExpiresAt(""); setData({ notifications: [], stations: [], history: [], sessions: [] });
    setTab("stations"); setStationId(null); setError(reason);
    setDataReady(false); setNetworkStale(false);
    await clearSession();
  }, []);
  const refresh = useCallback(async (signal?: AbortSignal) => {
    const current = tokenRef.current;
    try { await loaderRef.current!.refresh(signal); }
    catch (failure) {
      if (current !== tokenRef.current) return;
      setNetworkStale(true);
      if (failure instanceof Error && failure.message === "administratorAccount") { await expire("administratorAccount"); return; }
      throw failure;
    }
  }, [expire]);
  const boot = useCallback(async () => {
    if (bootController.current) return;
    const controller = new AbortController(); bootController.current = controller;
    setLoading(true); setWarming(false); setError("");
    try {
      const stored = await readSession();
      if (controller.signal.aborted) return;
      tokenRef.current = stored?.token ?? null; setToken(stored?.token ?? null); setExpiresAt(stored?.expiresAt ?? "");
      loaderRef.current!.setSession(stored?.token ?? null);
      if (stored) {
        if (!await waitForMobileApi(controller.signal, () => setWarming(true))) throw new Error("service_unavailable");
        await refresh(controller.signal);
      }
    } catch (failure) { if (!controller.signal.aborted) setError(failure instanceof Error ? failure.message : "genericError"); }
    finally { if (bootController.current === controller) { bootController.current = null; setLoading(false); setWarming(false); } }
  }, [refresh]);
  useEffect(() => { void boot(); return () => { bootController.current?.abort(); bootController.current = null; loaderRef.current!.setSession(null); }; }, [boot]);
  useEffect(() => subscribeSessionExpiry(value => { if (value === tokenRef.current) return expire("session_expired"); }), [expire]);
  useEffect(() => {
    if (!token || !expiresAt) return;
    const timer = setTimeout(() => void expire("session_expired").catch(() => setError("genericError")), Math.max(0, Date.parse(expiresAt) - Date.now()));
    return () => clearTimeout(timer);
  }, [token, expiresAt, expire]);
  useForegroundPoll(async signal => { try { await refresh(signal); } catch (failure) { if (!signal.aborted) setError(failure instanceof Error ? failure.message : "genericError"); } }, 15000, !!token && !!user, token ?? "");
  const pendingNotification = useRef(false);
  useEffect(() => {
    if (!pushAvailable) return;
    const open = () => {
      if (tokenRef.current) { setTab("notifications"); setStationId(null); void refresh().catch(() => {}); }
      else pendingNotification.current = true;
    };
    const received = Notifications.addNotificationReceivedListener(() => { void refresh().catch(() => {}); });
    const response = Notifications.addNotificationResponseReceivedListener(open);
    void Notifications.getLastNotificationResponseAsync().then(value => { if (value) { open(); void Notifications.clearLastNotificationResponseAsync(); } }).catch(() => {});
    return () => { received.remove(); response.remove(); };
  }, [refresh]);
  useEffect(() => {
    if (user && pendingNotification.current) { pendingNotification.current = false; setTab("notifications"); }
  }, [user]);
  useEffect(() => {
    if (user?.pushEnabled && token) void registerPush(token, false).catch(failure => {
      if (tokenRef.current === token) setError(failure instanceof Error ? failure.message : "genericError");
    });
  }, [user?.pushEnabled, token]);
  useEffect(() => {
    const back = BackHandler.addEventListener("hardwareBackPress", () => {
      if (stationId) { setStationId(null); return true; }
      if (tab === "settings" || tab === "notifications") { setTab("profile"); return true; }
      if (tab !== "stations") { setTab("stations"); return true; }
      return false;
    });
    return () => back.remove();
  }, [stationId, tab]);
  async function login(auth: AuthResult) {
    if (auth.user.role !== "DRIVER") {
      await request("/auth/logout", auth.accessToken, "POST").catch(() => {});
      throw new Error("administratorAccount");
    }
    await saveSession({ token: auth.accessToken, expiresAt: auth.expiresAt });
    tokenRef.current = auth.accessToken; loaderRef.current!.setSession(auth.accessToken);
    setToken(auth.accessToken); setExpiresAt(auth.expiresAt); updateUser(auth.user); setError("");
    setTab("stations"); setStationId(null); setData({ notifications: [], stations: [], history: [], sessions: [] });
    setDataReady(false); setNetworkStale(false);
    void refresh().catch(failure => { if (auth.accessToken === tokenRef.current) setError(failure instanceof Error ? failure.message : "genericError"); });
  }
  if (loading) return <View style={[styles.content, { justifyContent: "center" }]}><Brand/><ActivityIndicator color={colors.green}/><Text accessibilityLiveRegion="polite" style={styles.body}>{warming ? t.warming : t.loading}</Text></View>;
  if (!user && token) return <Screen><Text style={styles.brand}>EVCharging</Text><Notice text={errorText(new Error(error), language)}/><Button title={t.retry} onPress={() => void boot()}/><Button title={t.logout} secondary onPress={() => void expire().catch(() => setError("genericError"))}/></Screen>;
  if (!user || !token) return <View style={{ flex: 1 }}>{error && <View style={{ paddingHorizontal: 20 }}><Notice text={errorText(new Error(error), language)}/></View>}<AuthScreen language={language} setLanguage={setLanguage} onAuthenticated={login}/></View>;
  return <View style={{ flex: 1 }}>
    <View style={[styles.row, { paddingHorizontal: 20, paddingVertical: 10, flexWrap: "wrap", backgroundColor: colors.white }]}>
      <Brand/>
      <Pressable accessibilityRole="button" onPress={() => { setStationId(null); setTab("notifications"); }} style={{ minHeight: 44, justifyContent: "center" }}><Text style={styles.label}>{t.notifications} · {data.notifications.length}</Text></Pressable>
    </View>
    {error ? <View style={{ paddingHorizontal: 20 }}><Notice text={errorText(new Error(error), language)}/></View> : null}
    {!dataReady && tab !== "profile" && tab !== "settings" ? <Screen language={language}><ActivityIndicator color={colors.green}/><Text style={styles.body}>{t.loading}</Text><Button title={t.retry} secondary onPress={() => void refresh().catch(failure => setError(failure instanceof Error ? failure.message : "genericError"))}/></Screen> :
      stationId ? <StationChargingScreen key={`${token}:${stationId}:${chargingConnector}`} stationId={stationId} token={token} language={language} initialConnector={chargingConnector} onConnectorChange={setChargingConnector} onSessionEnded={sessionEnded} onBack={() => setStationId(null)} onOpenProfile={() => { setStationId(null); setTab("profile"); }}/> :
      tab === "stations" ? <StationsScreen stations={data.stations} stale={networkStale} language={language} refresh={refresh} onSelect={id => { setChargingConnector(1); setStationId(id); }}/> :
      tab === "history" ? <HistoryScreen history={data.history} language={language} refresh={refresh}/> :
      tab === "profile" ? <Screen language={language}><View style={styles.row}><Text style={styles.title}>{t.overview}</Text><Button title={t.settings} secondary onPress={() => setTab("settings")}/></View><ProfileScreen key={user.id} user={user} token={token} onUpdate={updateUser} language={language}/></Screen> :
      tab === "settings" ? <SettingsScreen user={user} token={token} onUpdate={updateUser} onExpired={() => expire("passwordChanged")} onBack={() => setTab("profile")} refresh={refresh} onLogout={async () => { try { await request("/auth/logout", token, "POST"); } finally { await expire(); } }}/> :
      <NotificationsScreen notifications={data.notifications} language={language} refresh={refresh}/>}
    <ActiveChargingShortcut sessions={data.sessions} language={language} currentStationId={stationId} currentConnector={chargingConnector} stale={networkStale}
      onOpen={session => { setChargingConnector(session.connectorId); setStationId(session.stationId); setTab("stations"); }}/>
    <View style={styles.tabs}>{(["stations", "history", "profile"] as const).map((value, index) => <Pressable accessibilityRole="tab" accessibilityState={{ selected: tab === value || (value === "profile" && (tab === "settings" || tab === "notifications")) }} key={value} onPress={() => { setStationId(null); setTab(value); }} style={styles.tab}>
      <Text style={[styles.tabText, { fontSize: 22 }, (tab === value || (value === "profile" && (tab === "settings" || tab === "notifications"))) && styles.activeTab]}>{["⌖", "◷", "○"][index]}</Text>
      <Text style={[styles.tabText, (tab === value || (value === "profile" && (tab === "settings" || tab === "notifications"))) && styles.activeTab]}>{value === "profile" ? t.overview : t[value]}</Text>
    </Pressable>)}</View>
  </View>;
}
export default function App() { return <SafeAreaProvider><SafeAreaView style={styles.screen}><StatusBar barStyle="dark-content"/><AppContent/></SafeAreaView></SafeAreaProvider>; }
