import { useEffect, useRef, useState } from "react";
import { Text, View } from "react-native";
import type { Language, AuthResult } from "../../contracts/account";
import { request } from "./api";
import { waitForMobileApi } from "./wait-for-api";
import { messages, errorText } from "./i18n";
import { Brand, Field, Button, Chip, Notice, Screen, styles } from "./ui";
import { PrivacyLinks } from "./PrivacyLinks";

export function AuthScreen({ language, setLanguage, onAuthenticated }: { language: Language; setLanguage: (value: Language) => void; onAuthenticated: (result: AuthResult) => Promise<void> }) {
  const [register, setRegister] = useState(false);
  const [email, setEmail] = useState(""), [password, setPassword] = useState(""), [confirmation, setConfirmation] = useState("");
  const [firstName, setFirstName] = useState(""), [lastName, setLastName] = useState(""), [phone, setPhone] = useState("");
  const [busy, setBusy] = useState(false), [warming, setWarming] = useState(false), [error, setError] = useState("");
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => pending.current?.abort(), []);
  const t = messages[language];
  async function submit() {
    if (pending.current) return;
    const controller = new AbortController(); pending.current = controller;
    setBusy(true); setError(""); setWarming(false);
    try {
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) throw new Error("invalid_email");
      if (!password || (register && password.length < 12)) throw new Error("invalid_password");
      if (register && (!firstName.trim() || !lastName.trim())) throw new Error("invalid_name");
      if (register && password !== confirmation) throw new Error("passwordMismatch");
      if (!await waitForMobileApi(controller.signal, () => setWarming(true))) throw new Error("service_unavailable");
      setWarming(false);
      const result = await request<AuthResult>(register ? "/auth/register" : "/auth/login", null, "POST",
        register ? { email: email.trim(), password, firstName: firstName.trim(), lastName: lastName.trim(), phone: phone.trim(), language } : { email: email.trim(), password }, controller.signal);
      if (!controller.signal.aborted) await onAuthenticated(result);
      setPassword(""); setConfirmation("");
    } catch (failure) { if (!controller.signal.aborted) setError(errorText(failure, language)); }
    finally { if (pending.current === controller) { pending.current = null; setBusy(false); setWarming(false); } }
  }
  return <Screen language={language}>
    <View style={[styles.row, { flexWrap: "wrap" }]}><Brand/><View style={styles.wrap}>{(["sq", "en"] as const).map(value => <Chip key={value} title={value.toUpperCase()} selected={language === value} disabled={busy} onPress={() => { setLanguage(value); setError(""); }}/>)}</View></View>
    <Text style={styles.eyebrow}>CONNECTED ENERGY</Text><Text style={styles.title}>{t.welcome}</Text><Text style={styles.body}>{t.intro}</Text>
    <View style={styles.wrap}><Chip title={t.signIn} selected={!register} onPress={() => { setRegister(false); setPassword(""); setConfirmation(""); setError(""); }} disabled={busy}/><Chip title={t.register} selected={register} onPress={() => { setRegister(true); setPassword(""); setError(""); }} disabled={busy}/></View>
    {register && <><Field label={t.firstName} value={firstName} onChangeText={setFirstName} autoComplete="given-name" maxLength={80} editable={!busy}/><Field label={t.lastName} value={lastName} onChangeText={setLastName} autoComplete="family-name" maxLength={80} editable={!busy}/></>}
    <Field label={t.email} value={email} onChangeText={setEmail} autoCapitalize="none" autoCorrect={false} keyboardType="email-address" autoComplete="email" maxLength={254} editable={!busy}/>
    {register && <Field label={t.phone} value={phone} onChangeText={setPhone} keyboardType="phone-pad" autoComplete="tel" placeholder="+355691234567" maxLength={32} editable={!busy}/>}
    <Field label={t.password} value={password} onChangeText={setPassword} secureTextEntry autoCapitalize="none" autoCorrect={false} autoComplete={register ? "new-password" : "current-password"} maxLength={128} editable={!busy}/>
    {register && <><Field label={t.confirmPassword} value={confirmation} onChangeText={setConfirmation} secureTextEntry autoComplete="new-password" maxLength={128} editable={!busy}/><Text style={styles.body}>{t.passwordHint}</Text></>}
    <Notice text={error}/>{warming && <Text accessibilityLiveRegion="polite" style={styles.body}>{t.warming}</Text>}
    <Button title={busy ? t.loading : register ? t.create : t.signIn} disabled={busy} onPress={() => void submit()}/>
    <PrivacyLinks language={language}/>
  </Screen>;
}
