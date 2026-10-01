import { useCallback, useEffect, useRef, useState } from "react";
import { ActivityIndicator, AppState, Pressable, ScrollView, Text, View } from "react-native";
import { SafeAreaProvider, SafeAreaView } from "react-native-safe-area-context";
import * as Notifications from "expo-notifications";
import type { AuthResult, ChargingHistory, Language, Notification, Station, User } from "../contracts/account";
import { readSession, saveSession, clearSession, request } from "./src/api";
import { messages, errorText } from "./src/i18n";
import { AuthScreen } from "./src/AuthScreen";
import { ProfileScreen } from "./src/ProfileScreen";
import { SettingsScreen } from "./src/SettingsScreen";
import { StationsScreen } from "./src/StationsScreen";
import { HistoryScreen } from "./src/HistoryScreen";
import { registerPush } from "./src/push";
import { Button, Card, Notice, styles, colors } from "./src/ui";

type Tab = "stations" | "history" | "profile" | "notifications" | "settings";
function AppContent() {
  const [language, setLanguage] = useState<Language>("sq");
  const [user, setUser] = useState<User | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const tokenRef = useRef<string | null>(null);
  const [tab, setTab] = useState<Tab>("stations");
  const [loading, setLoading] = useState(true); const [error, setError] = useState("");
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [stations, setStations] = useState<Station[]>([]);
  const [history, setHistory] = useState<ChargingHistory[]>([]);
  const t = messages[language];
  const expire = useCallback(async () => {
    tokenRef.current = null; setToken(null); setUser(null); setNotifications([]); setStations([]); setHistory([]); setTab("stations");
    await clearSession();
  }, []);
  const updateUser = useCallback((value: User) => { setUser(value); setLanguage(value.language); }, []);
  const refresh = useCallback(async () => {
    const value = tokenRef.current;
    if (!value) return;
    try {
      const current = await request<User>("/me", value);
      if (value !== tokenRef.current) return;
      updateUser(current);
      const [inbox,points,sessions] = await Promise.all([
        request<Notification[]>("/me/notifications", value),
        request<Station[]>("/driver/stations", value),
        request<ChargingHistory[]>("/driver/history", value)
      ]);
      if (value === tokenRef.current) {
        setNotifications(inbox); setStations(points); setHistory(sessions); setError("");
      }
    } catch (e) {
      if (value !== tokenRef.current) return;
      if (e instanceof Error && e.message === "session_expired") await expire();
      throw e;
    }
  }, [expire, updateUser]);
  const boot = useCallback(async () => {
    setLoading(true); setError("");
    try {
      const stored = await readSession();
      tokenRef.current = stored?.token || null; setToken(stored?.token || null);
      if (stored) await refresh();
    } catch (e) { setError(e instanceof Error ? e.message : "genericError"); }
    finally { setLoading(false); }
  }, [refresh]);
  useEffect(() => { void boot(); }, [boot]);
  useEffect(() => {
    const resume = AppState.addEventListener("change", state => {
      if (state === "active") void refresh().catch(e => setError(e instanceof Error ? e.message : "genericError"));
    });
    const received = Notifications.addNotificationReceivedListener(() => { void refresh().catch(() => {}); });
    const response = Notifications.addNotificationResponseReceivedListener(() => { if (tokenRef.current) { setTab("notifications"); void refresh().catch(() => {}); } });
    return () => { resume.remove(); received.remove(); response.remove(); };
  }, [refresh]);
  useEffect(() => {
    if (user?.pushEnabled && token) void registerPush(token, false).catch(e => setError(e instanceof Error ? e.message : "genericError"));
  }, [user?.pushEnabled, token]);
  const login = async (auth: AuthResult) => {
    if (auth.user.role !== "DRIVER") {
      await request("/auth/logout", auth.accessToken, "POST");
      throw new Error("administratorAccount");
    }
    await saveSession({ token: auth.accessToken, expiresAt: auth.expiresAt });
    tokenRef.current = auth.accessToken; setToken(auth.accessToken); updateUser(auth.user); setError("");
    setTab("stations"); setNotifications([]); setStations([]); setHistory([]);
    void refresh().catch(e => setError(e instanceof Error ? e.message : "genericError"));
  };
  if (loading) return <View style={[styles.content, { flex: 1, justifyContent: "center" }]}><ActivityIndicator color={colors.green}/><Text style={styles.body}>{t.loading}</Text></View>;
  if (!user && token) return <View style={styles.content}><Text style={styles.brand}>EVCharging</Text><Notice text={errorText(new Error(error), language)}/><Button title={t.retry} onPress={() => { void boot(); }}/></View>;
  if (!user || !token) return <AuthScreen language={language} setLanguage={setLanguage} onAuthenticated={login}/>;
  return <View style={{ flex: 1 }}>
    {error ? <View style={{ paddingHorizontal: 24 }}><Notice text={errorText(new Error(error), language)}/></View> : null}
    {tab === "stations" ? <StationsScreen stations={stations} language={language} refresh={refresh}/> :
    tab === "history" ? <HistoryScreen history={history} language={language} refresh={refresh}/> :
    tab === "profile" ? <ProfileScreen key={user.version} user={user} token={token} onUpdate={updateUser} language={language} refresh={refresh}/> :
    tab === "settings" ? <SettingsScreen user={user} token={token} onUpdate={updateUser} onExpired={expire} refresh={refresh} onLogout={async () => { await request("/auth/logout", token, "POST"); await expire(); }}/> :
    <ScrollView contentContainerStyle={styles.content}><Text style={styles.title}>{t.notifications}</Text>{notifications.length ? notifications.map(item => <Card key={item.id}><Text style={styles.label}>{item.text.title}</Text><Text style={styles.body}>{item.text.body}</Text><Text style={styles.body}>{new Date(item.createdAt).toLocaleString(language === "sq" ? "sq-AL" : "en-GB")}</Text></Card>) : <Card><Text style={styles.body}>{t.emptyNotifications}</Text></Card>}<Button title={t.refresh} secondary onPress={() => { void refresh().catch(e => setError(e instanceof Error ? e.message : "genericError")); }}/></ScrollView>}
    <View style={styles.tabs}>{(["stations", "history", "profile", "notifications", "settings"] as const).map(value => <Pressable accessibilityRole="tab" accessibilityState={{ selected: tab === value }} key={value} onPress={() => { setTab(value); if (value === "notifications") void refresh().catch(e => setError(e instanceof Error ? e.message : "genericError")); }} style={styles.tab}><Text style={[styles.tabText, tab === value && styles.activeTab]} numberOfLines={1}>{t[value]}</Text></Pressable>)}</View>
  </View>;
}
export default function App() { return <SafeAreaProvider><SafeAreaView style={styles.screen}><AppContent/></SafeAreaView></SafeAreaProvider>; }
