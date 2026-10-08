import { useRef, useState } from "react";
import { Linking, View, Text, Switch } from "react-native";
import type { Language, User } from "../../contracts/account";
import { request } from "./api";
import { registerPush } from "./push";
import { messages, errorText } from "./i18n";
import { Field, Button, Notice, Card, Chip, Screen, styles, colors } from "./ui";

export function SettingsScreen({ user, token, onUpdate, onLogout, onExpired, refresh, onBack }: { user: User; token: string; onUpdate: (value: User) => void; onLogout: () => Promise<void>; onExpired: () => Promise<void>; refresh: () => Promise<void>; onBack: () => void }) {
  const busyRef = useRef(false);
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  const [currentPassword, setCurrentPassword] = useState(""), [newPassword, setNewPassword] = useState(""), [confirmation, setConfirmation] = useState("");
  const t = messages[user.language];
  async function perform(action: () => Promise<void>) {
    if (busyRef.current) return;
    busyRef.current = true; setBusy(true); setError("");
    try { await action(); } catch (failure) { setError(failure instanceof Error ? failure.message : "genericError"); }
    finally { busyRef.current = false; setBusy(false); }
  }
  async function update(language: Language, pushEnabled: boolean) {
    await perform(async () => {
      if (pushEnabled && !user.pushEnabled) await registerPush(token, true);
      const updated = await request<User>("/me/settings", token, "PATCH", { language, pushEnabled, version: user.version });
      onUpdate(updated);
      if (!pushEnabled) await request("/me/devices", token, "DELETE");
    });
  }
  return <Screen language={user.language}>
    <Button title={user.language === "sq" ? "← Kthehu te llogaria" : "← Back to account"} secondary onPress={onBack}/>
    <Text style={styles.title}>{t.settings}</Text>
    <Card><Text style={styles.label}>{t.language}</Text><View style={styles.wrap}>{(["sq", "en"] as const).map(language => <Chip title={language === "sq" ? "Shqip" : "English"} selected={language === user.language} disabled={busy} key={language} onPress={() => void update(language, user.pushEnabled)}/>)}</View></Card>
    <Card><View style={styles.row}><Text style={[styles.label, { flex: 1 }]}>{t.push}</Text><Switch accessibilityLabel={t.push} value={user.pushEnabled} disabled={busy} onValueChange={value => void update(user.language, value)} trackColor={{ true: colors.green }}/></View><Text style={styles.body}>{t.pushHint}</Text></Card>
    <Notice text={error ? errorText(new Error(error), user.language) : ""}/>
    {error === "pushDenied" && <Button title={user.language === "sq" ? "Hap cilësimet e telefonit" : "Open phone settings"} secondary onPress={() => void Linking.openSettings().catch(() => setError("genericError"))}/>}
    <Card><Text style={[styles.label, { fontSize: 18 }]}>{t.changePassword}</Text>
      <Field label={t.currentPassword} value={currentPassword} onChangeText={setCurrentPassword} secureTextEntry autoComplete="current-password" maxLength={128} editable={!busy}/>
      <Field label={t.newPassword} value={newPassword} onChangeText={setNewPassword} secureTextEntry autoComplete="new-password" maxLength={128} editable={!busy}/>
      <Field label={t.confirmPassword} value={confirmation} onChangeText={setConfirmation} secureTextEntry autoComplete="new-password" maxLength={128} editable={!busy}/>
      <Text style={styles.body}>{t.passwordHint}</Text>
      <Button title={busy ? t.working : t.changePassword} disabled={busy || !currentPassword || !newPassword || !confirmation} onPress={() => void perform(async () => {
        if (newPassword.length < 12) throw new Error("invalid_password");
        if (newPassword !== confirmation) throw new Error("passwordMismatch");
        await request("/auth/password", token, "POST", { currentPassword, newPassword });
        setCurrentPassword(""); setNewPassword(""); setConfirmation(""); await onExpired();
      })}/>
    </Card>
    <Button title={t.refresh} secondary disabled={busy} onPress={() => void perform(refresh)}/>
    <Button title={t.logout} secondary disabled={busy} onPress={() => void perform(onLogout)}/>
  </Screen>;
}
