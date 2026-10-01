import { useState } from "react";
import { ScrollView, View, Text, Switch, Pressable } from "react-native";
import type { Language, User } from "../../contracts/account";
import { request } from "./api";
import { registerPush } from "./push";
import { messages, errorText } from "./i18n";
import { Field, Button, Notice, Card, styles, colors } from "./ui";

export function SettingsScreen({ user, token, onUpdate, onLogout, onExpired, refresh }: { user: User; token: string; onUpdate: (value: User) => void; onLogout: () => Promise<void>; onExpired: () => Promise<void>; refresh: () => Promise<void> }) {
  const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const [currentPassword, setCurrentPassword] = useState(""); const [newPassword, setNewPassword] = useState("");
  const t = messages[user.language];
  async function update(language: Language, pushEnabled: boolean) {
    if (busy) return;
    setBusy(true); setError("");
    try {
      if (pushEnabled && !user.pushEnabled) await registerPush(token, true);
      const updated = await request<User>("/me/settings", token, "PATCH", { language, pushEnabled, version: user.version });
      onUpdate(updated);
      if (!pushEnabled) await request("/me/devices", token, "DELETE");
    } catch (e) { setError(errorText(e, user.language)); } finally { setBusy(false); }
  }
  return <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={styles.content}><Text style={styles.title}>{t.settings}</Text>
    <Card><Text style={styles.label}>{t.language}</Text><View style={styles.row}>{(["sq", "en"] as const).map(language => <Pressable accessibilityRole="button" accessibilityState={{ selected: language === user.language, disabled: busy }} disabled={busy} key={language} onPress={() => void update(language, user.pushEnabled)}><Text style={language === user.language ? styles.tag : styles.body}>{language === "sq" ? "Shqip" : "English"}</Text></Pressable>)}</View></Card>
    <Card><View style={styles.row}><Text style={styles.label}>{t.push}</Text><Switch accessibilityLabel={t.push} value={user.pushEnabled} disabled={busy} onValueChange={value => void update(user.language, value)} trackColor={{ true: colors.green }}/></View><Text style={styles.body}>{t.pushHint}</Text></Card>
    <Notice text={error}/>
    <Card><Text style={styles.label}>{t.changePassword}</Text><Field label={t.currentPassword} value={currentPassword} onChangeText={setCurrentPassword} secureTextEntry maxLength={128} editable={!busy}/><Field label={t.newPassword} value={newPassword} onChangeText={setNewPassword} secureTextEntry maxLength={128} editable={!busy}/>
    <Button title={busy ? t.working : t.changePassword} disabled={busy || !currentPassword || newPassword.length < 12} onPress={async () => {
      setBusy(true); setError("");
      try { await request("/auth/password", token, "POST", { currentPassword, newPassword }); await onExpired(); }
      catch (e) { setError(errorText(e, user.language)); } finally { setBusy(false); }
    }}/></Card>
    <Button title={t.refresh} secondary disabled={busy} onPress={() => { void refresh().catch(e => setError(errorText(e, user.language))); }}/>
    <Button title={t.logout} secondary disabled={busy} onPress={async () => {
      setBusy(true); setError("");
      try { await onLogout(); } catch (e) { setError(errorText(e, user.language)); } finally { setBusy(false); }
    }}/>
  </ScrollView>;
}
