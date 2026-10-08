import { useRef, useState } from "react";
import { Text, View } from "react-native";
import type { User, Language } from "../../contracts/account";
import { request } from "./api";
import { profileDraft, profilePayload } from "./account-inputs";
import { messages, errorText } from "./i18n";
import { Field, Button, Notice, Card, styles } from "./ui";

export function ProfileScreen({ user, token, onUpdate, language }: { user: User; token: string; onUpdate: (value: User) => void; language: Language }) {
  const [draft, setDraft] = useState(() => profileDraft(user));
  const [version, setVersion] = useState(user.version);
  const busyRef = useRef(false);
  const [busy, setBusy] = useState(false), [notice, setNotice] = useState(""), [success, setSuccess] = useState(false);
  const t = messages[language];
  async function update(reload: boolean) {
    if (busyRef.current) return;
    busyRef.current = true; setBusy(true); setNotice(""); setSuccess(false);
    try {
      const result = reload ? await request<User>("/me", token) : await request<User>("/me/profile", token, "PATCH", profilePayload(draft, version));
      setDraft(profileDraft(result)); setVersion(result.version); onUpdate(result);
      if (!reload) { setNotice(t.saved); setSuccess(true); }
    } catch (failure) { setNotice(errorText(failure, language)); }
    finally { busyRef.current = false; setBusy(false); }
  }
  return <View style={{ gap: 16 }}><Text style={styles.body}>{t.profileIntro}</Text>
    <Card><Text style={[styles.label, { fontSize: 18 }]}>{t.profile}</Text>
      {([["firstName", t.firstName], ["lastName", t.lastName], ["phone", t.phone]] as const).map(([key, label]) => <Field key={key} label={label} value={draft[key]} onChangeText={value => setDraft(previous => ({ ...previous, [key]: value }))} keyboardType={key === "phone" ? "phone-pad" : "default"} autoComplete={key === "phone" ? "tel" : key === "firstName" ? "given-name" : "family-name"} maxLength={key === "phone" ? 32 : 80} editable={!busy}/>)}
      <Text style={styles.body}>{user.email}</Text>
    </Card>
    <Card><Text style={[styles.label, { fontSize: 18 }]}>{language === "sq" ? "Automjeti" : "Vehicle"}</Text>
      {([["vehicleMake", t.make], ["vehicleModel", t.model], ["batteryType", t.battery]] as const).map(([key, label]) => <Field key={key} label={label} value={draft[key]} onChangeText={value => setDraft(previous => ({ ...previous, [key]: value }))} maxLength={80} editable={!busy}/>)}
      <Field label={t.batteryCapacity} value={draft.batteryCapacityKWh} onChangeText={value => setDraft(previous => ({ ...previous, batteryCapacityKWh: value }))} keyboardType="decimal-pad" placeholder="77.5" maxLength={12} editable={!busy}/>
      <Text style={styles.body}>{t.capacityHint}</Text>
    </Card>
    <Notice text={notice} success={success}/>
    <Button title={busy ? t.working : t.save} disabled={busy} onPress={() => void update(false)}/>
    <Button title={t.refresh} secondary disabled={busy} onPress={() => void update(true)}/>
  </View>;
}
