import { useState } from "react";
import { ScrollView, Text } from "react-native";
import type { User, Language } from "../../contracts/account";
import { request } from "./api";
import { messages, errorText } from "./i18n";
import { Field, Button, Notice, styles } from "./ui";

export function ProfileScreen({ user, token, onUpdate, language, refresh }: { user: User; token: string; onUpdate: (value: User) => void; language: Language; refresh: () => Promise<void> }) {
  const [draft, setDraft] = useState({ firstName: user.firstName, lastName: user.lastName, phone: user.phone || "", vehicleMake: user.vehicleMake || "", vehicleModel: user.vehicleModel || "", batteryType: user.batteryType || "" });
  const [version] = useState(user.version);
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState(""); const [success, setSuccess] = useState(false);
  const t = messages[language];
  return <ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={styles.content}><Text style={styles.title}>{t.profile}</Text><Text style={styles.body}>{t.profileIntro}</Text>
    {([["firstName", t.firstName], ["lastName", t.lastName], ["phone", t.phone], ["vehicleMake", t.make], ["vehicleModel", t.model], ["batteryType", t.battery]] as const).map(([key, label]) => <Field key={key} label={label} value={draft[key]} onChangeText={value => setDraft(previous => ({ ...previous, [key]: value }))} maxLength={key === "phone" ? 32 : 80} editable={!busy}/>)}
    <Text style={styles.body}>{user.email}</Text><Notice text={notice} success={success}/>
    <Button title={busy ? t.working : t.save} disabled={busy} onPress={async () => {
      setBusy(true); setNotice(""); setSuccess(false);
      try { const result = await request<User>("/me/profile", token, "PATCH", { ...draft, version }); onUpdate(result); setNotice(t.saved); setSuccess(true); }
      catch (e) { setNotice(errorText(e, language)); } finally { setBusy(false); }
    }}/><Button title={t.refresh} secondary disabled={busy} onPress={() => { void refresh().catch(e => setNotice(errorText(e, language))); }}/>
  </ScrollView>;
}
