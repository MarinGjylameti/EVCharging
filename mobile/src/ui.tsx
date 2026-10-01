import type { PropsWithChildren } from "react";
import { StyleSheet, Text, TextInput, Pressable, View, type TextInputProps } from "react-native";

export const colors = { background: "#f7f8f2", ink: "#153b2d", muted: "#748375", green: "#226947", lime: "#d5edac", border: "#dce4d7", white: "#ffffff", danger: "#973a2e" };
export function Field({ label, ...props }: TextInputProps & { label: string }) {
  return <View style={styles.field}><Text style={styles.label}>{label}</Text><TextInput accessibilityLabel={label} placeholderTextColor="#9ca898" style={styles.input} {...props}/></View>;
}
export function Button({ title, onPress, disabled, secondary }: { title: string; onPress: () => void; disabled?: boolean; secondary?: boolean }) {
  return <Pressable accessibilityRole="button" disabled={disabled} onPress={onPress} style={[styles.button, secondary && styles.secondary, disabled && { opacity: .6 }]}><Text style={[styles.buttonText, secondary && { color: colors.ink }]}>{title}</Text></Pressable>;
}
export function Card({ children }: PropsWithChildren) { return <View style={styles.card}>{children}</View>; }
export function Notice({ text, success = false }: { text: string; success?: boolean }) {
  return text ? <Text accessibilityRole="alert" style={[styles.notice, success && { color: colors.green, backgroundColor: "#e9f3e0" }]}>{text}</Text> : null;
}
export const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  content: { padding: 24, paddingBottom: 45, gap: 16 },
  brand: { fontSize: 23, fontWeight: "800", color: colors.ink, letterSpacing: -.8 },
  eyebrow: { fontSize: 10, letterSpacing: 2, color: colors.green, fontWeight: "700", marginTop: 20 },
  title: { fontSize: 31, lineHeight: 38, fontWeight: "700", letterSpacing: -1.2, color: colors.ink },
  body: { fontSize: 14, lineHeight: 23, color: colors.muted },
  field: { gap: 8, marginVertical: 6 },
  label: { fontSize: 12, fontWeight: "600", color: colors.ink },
  input: { borderWidth: 1, borderColor: colors.border, backgroundColor: colors.white, padding: 14, borderRadius: 10, color: colors.ink, fontSize: 15 },
  button: { backgroundColor: colors.green, borderRadius: 10, minHeight: 50, justifyContent: "center", alignItems: "center", padding: 13, marginVertical: 5 },
  buttonText: { color: colors.white, fontWeight: "700", fontSize: 14 },
  secondary: { backgroundColor: colors.white, borderWidth: 1, borderColor: colors.border },
  card: { padding: 20, borderWidth: 1, borderColor: colors.border, borderRadius: 16, backgroundColor: colors.white, gap: 12 },
  row: { flexDirection: "row", alignItems: "center", justifyContent: "space-between", gap: 15 },
  notice: { backgroundColor: "#fff0ed", color: colors.danger, padding: 12, borderRadius: 8, fontSize: 13, lineHeight: 21 },
  tabs: { flexDirection: "row", borderTopWidth: 1, borderTopColor: colors.border, backgroundColor: colors.white },
  tab: { flex: 1, paddingVertical: 19, alignItems: "center" },
  tabText: { fontSize: 11, color: colors.muted, fontWeight: "600" },
  activeTab: { color: colors.green },
  tag: { paddingVertical: 7, paddingHorizontal: 11, borderRadius: 20, overflow: "hidden", backgroundColor: "#edf3e6", color: colors.green, fontSize: 11, alignSelf: "flex-start" },
  divider: { borderTopWidth: 1, borderTopColor: colors.border, marginVertical: 14 }
});
