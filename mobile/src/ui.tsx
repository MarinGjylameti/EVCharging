import { useRef, useState, type PropsWithChildren, type Ref } from "react";
import { Image, KeyboardAvoidingView, Modal, Platform, RefreshControl, ScrollView, StyleSheet, Text, TextInput, Pressable, View, type TextInputProps } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import type { Language } from "../../contracts/account";
import { errorText } from "./i18n";

export const colors = { background: "#f7f8f2", ink: "#153b2d", muted: "#536b5e", green: "#226947", lime: "#d5edac", border: "#dce4d7", white: "#ffffff", danger: "#973a2e" };
export function Brand() {
  return <View style={{ flexDirection: "row", alignItems: "center", gap: 9 }}><Image source={require("../assets/icon.png")} accessible={false} style={{ width: 36, height: 36, borderRadius: 9 }}/><Text style={[styles.brand, { fontSize: 21 }]}>EVCharging</Text></View>;
}
export function Field({ label, ...props }: TextInputProps & { label: string }) {
  return <View style={styles.field}><Text style={styles.label}>{label}</Text><TextInput accessibilityLabel={label} placeholderTextColor="#9ca898" style={styles.input} {...props}/></View>;
}
export function Button({ title, onPress, disabled, secondary }: { title: string; onPress: () => void; disabled?: boolean; secondary?: boolean }) {
  return <Pressable accessibilityRole="button" accessibilityState={{ disabled: !!disabled }} disabled={disabled} onPress={onPress} style={({ pressed }) => [styles.button, secondary && styles.secondary, (disabled || pressed) && { opacity: .6 }]}><Text style={[styles.buttonText, secondary && { color: colors.ink }]}>{title}</Text></Pressable>;
}
export function Card({ children }: PropsWithChildren) { return <View style={styles.card}>{children}</View>; }
export function Notice({ text, success = false }: { text: string; success?: boolean }) {
  return text ? <Text accessibilityRole="alert" style={[styles.notice, success && { color: colors.green, backgroundColor: "#e9f3e0" }]}>{text}</Text> : null;
}
export function Screen({ children, language = "sq", refresh, scrollEnabled = true, scrollRef }: PropsWithChildren<{ language?: Language; refresh?: () => Promise<void>; scrollEnabled?: boolean; scrollRef?: Ref<ScrollView> }>) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const pending = useRef(false);
  async function reload() {
    if (pending.current || !refresh) return;
    pending.current = true; setBusy(true); setError("");
    try { await refresh(); } catch (failure) { setError(errorText(failure, language)); }
    finally { pending.current = false; setBusy(false); }
  }
  return <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === "ios" ? "padding" : undefined}>
    <ScrollView ref={scrollRef} scrollEnabled={scrollEnabled} keyboardShouldPersistTaps="handled" keyboardDismissMode="on-drag" contentContainerStyle={styles.content}
      refreshControl={refresh ? <RefreshControl refreshing={busy} onRefresh={() => void reload()} tintColor={colors.green} /> : undefined}>
      <Notice text={error}/>{children}
    </ScrollView>
  </KeyboardAvoidingView>;
}
export function Chip({ title, selected, onPress, disabled = false }: { title: string; selected: boolean; onPress: () => void; disabled?: boolean }) {
  return <Pressable accessibilityRole="button" accessibilityState={{ selected, disabled }} onPress={onPress} disabled={disabled} style={[styles.chip, selected && { backgroundColor: colors.green, borderColor: colors.green }]}><Text style={[styles.chipText, selected && { color: colors.white }]}>{title}</Text></Pressable>;
}
export function ChoiceField({ label, value, options, onChange, language }: {
  label: string; value: string; options: { value: string; label: string }[]; onChange: (value: string) => void; language: Language;
}) {
  const [open, setOpen] = useState(false);
  const insets = useSafeAreaInsets();
  const close = language === "sq" ? "Mbyll" : "Close";
  return <View style={styles.field}><Text style={styles.label}>{label}</Text>
    <Pressable accessibilityRole="button" accessibilityLabel={`${label}: ${options.find(item => item.value === value)?.label ?? value}`} onPress={() => setOpen(true)} style={[styles.input, styles.row]}><Text style={[styles.body, { color: colors.ink, flex: 1 }]}>{options.find(item => item.value === value)?.label ?? value}</Text><Text>⌄</Text></Pressable>
    <Modal transparent visible={open} animationType="slide" onRequestClose={() => setOpen(false)}>
      <View style={{ flex: 1, justifyContent: "flex-end" }}>
        <Pressable accessibilityRole="button" accessibilityLabel={close} onPress={() => setOpen(false)} style={[StyleSheet.absoluteFill, { backgroundColor: "#0006" }]}/>
        <View accessibilityViewIsModal style={{ maxHeight: "80%", backgroundColor: colors.white, borderTopLeftRadius: 22, borderTopRightRadius: 22, padding: 20, paddingBottom: insets.bottom + 20 }}>
          <View style={styles.row}><Text style={[styles.label, { fontSize: 18, flex: 1 }]}>{label}</Text><Button title={close} secondary onPress={() => setOpen(false)}/></View>
          <ScrollView>{options.map(item => <Pressable key={item.value} accessibilityRole="button" accessibilityState={{ selected: item.value === value }} onPress={() => { onChange(item.value); setOpen(false); }} style={[styles.row, { minHeight: 52, paddingVertical: 12, borderBottomWidth: 1, borderBottomColor: colors.border }]}><Text style={[styles.body, { flex: 1, color: colors.ink }]}>{item.label}</Text>{item.value === value && <Text style={styles.label}>✓</Text>}</Pressable>)}</ScrollView>
        </View>
      </View>
    </Modal>
  </View>;
}
export const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  content: { padding: 20, paddingBottom: 32, gap: 16, flexGrow: 1, maxWidth: 720, width: "100%", alignSelf: "center" },
  brand: { fontSize: 23, fontWeight: "800", color: colors.ink, letterSpacing: -.8 },
  eyebrow: { fontSize: 10, letterSpacing: 2, color: colors.green, fontWeight: "700", marginTop: 20 },
  title: { fontSize: 31, lineHeight: 38, fontWeight: "700", letterSpacing: -1.2, color: colors.ink },
  body: { fontSize: 14, lineHeight: 23, color: colors.muted },
  field: { gap: 8, marginVertical: 6 },
  label: { fontSize: 12, fontWeight: "600", color: colors.ink },
  input: { borderWidth: 1, borderColor: colors.border, backgroundColor: colors.white, minHeight: 48, padding: 14, borderRadius: 10, color: colors.ink, fontSize: 16 },
  button: { backgroundColor: colors.green, borderRadius: 10, minHeight: 50, justifyContent: "center", alignItems: "center", padding: 13, marginVertical: 5 },
  buttonText: { color: colors.white, fontWeight: "700", fontSize: 14, textAlign: "center", flexShrink: 1 },
  secondary: { backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border },
  card: { padding: 20, borderWidth: 1, borderColor: colors.border, borderRadius: 16, backgroundColor: colors.white, gap: 12 },
  row: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 15 },
  notice: { backgroundColor: "#fff0ed", color: colors.danger, padding: 12, borderRadius: 8, fontSize: 13, lineHeight: 21 },
  tabs: { flexDirection: "row", borderTopWidth: 1, borderTopColor: colors.border, backgroundColor: colors.white },
  tab: { flex: 1, minHeight: 60, paddingVertical: 10, paddingHorizontal: 3, gap: 4, alignItems: "center", justifyContent: "center" },
  tabText: { fontSize: 11, color: colors.muted, fontWeight: "600" },
  activeTab: { color: colors.green },
  tag: { paddingVertical: 7, paddingHorizontal: 11, borderRadius: 20, overflow: "hidden", backgroundColor: "#edf3e6", color: colors.green, fontSize: 11, alignSelf: "flex-start" },
  divider: { borderTopWidth: 1, borderTopColor: colors.border, marginVertical: 14 },
  chip: { minHeight: 44, paddingHorizontal: 14, paddingVertical: 10, justifyContent: "center", alignItems: "center", borderWidth: 1, borderColor: colors.border, borderRadius: 10, backgroundColor: colors.white },
  chipText: { fontSize: 12, fontWeight: "600", color: colors.ink },
  wrap: { flexDirection: "row", flexWrap: "wrap", gap: 8 },
  metric: { fontSize: 26, fontWeight: "700", color: colors.ink },
});
