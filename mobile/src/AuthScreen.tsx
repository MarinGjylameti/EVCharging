import { useState } from "react";
import { ScrollView, Text, View, Pressable, KeyboardAvoidingView, Platform } from "react-native";
import type { Language, AuthResult } from "../../contracts/account";
import { request } from "./api";
import { messages, errorText } from "./i18n";
import { Field, Button, Notice, styles } from "./ui";

export function AuthScreen({ language, setLanguage, onAuthenticated }: { language: Language; setLanguage: (value: Language) => void; onAuthenticated: (result: AuthResult) => Promise<void> }) {
  const [register, setRegister] = useState(false);
  const [email, setEmail] = useState(""); const [password, setPassword] = useState("");
  const [firstName, setFirstName] = useState(""); const [lastName, setLastName] = useState("");
  const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const t = messages[language];
  async function submit() {
    if (busy) return;
    setBusy(true); setError("");
    try {
      const result = await request<AuthResult>(register ? "/auth/register" : "/auth/login", null, "POST",
        register ? { email, password, firstName, lastName, language } : { email, password });
      await onAuthenticated(result);
      setPassword("");
    } catch (e) { setError(errorText(e, language)); }
    finally { setBusy(false); }
  }
  return <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === "ios" ? "padding" : undefined}><ScrollView keyboardShouldPersistTaps="handled" contentContainerStyle={styles.content}>
    <View style={styles.row}><Text style={styles.brand}>ϟ EVCharging</Text><View style={{ flexDirection: "row", gap: 14 }}>{(["sq", "en"] as const).map(value => <Pressable key={value} accessibilityRole="button" accessibilityState={{ selected: language === value }} onPress={() => { setLanguage(value); setError(""); }}><Text style={language === value ? styles.label : styles.body}>{value.toUpperCase()}</Text></Pressable>)}</View></View>
    <Text style={styles.eyebrow}>CONNECTED ENERGY</Text><Text style={styles.title}>{t.welcome}</Text><Text style={styles.body}>{t.intro}</Text>
    <View style={styles.row}><Button title={t.signIn} secondary={register} onPress={() => { setRegister(false); setError(""); }} disabled={busy}/><Button title={t.register} secondary={!register} onPress={() => { setRegister(true); setError(""); }} disabled={busy}/></View>
    {register ? <><Field label={t.firstName} value={firstName} onChangeText={setFirstName} autoComplete="given-name" maxLength={80} editable={!busy}/><Field label={t.lastName} value={lastName} onChangeText={setLastName} autoComplete="family-name" maxLength={80} editable={!busy}/></> : null}
    <Field label={t.email} value={email} onChangeText={setEmail} autoCapitalize="none" keyboardType="email-address" autoComplete="email" maxLength={254} editable={!busy}/>
    <Field label={t.password} value={password} onChangeText={setPassword} secureTextEntry autoComplete={register ? "new-password" : "current-password"} maxLength={128} editable={!busy}/>
    {register ? <Text style={styles.body}>{t.passwordHint}</Text> : null}<Notice text={error}/>
    <Button title={busy ? t.working : register ? t.create : t.signIn} disabled={busy} onPress={submit}/>
  </ScrollView></KeyboardAvoidingView>;
}
