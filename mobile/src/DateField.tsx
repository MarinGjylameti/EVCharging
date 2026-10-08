import { useState } from "react";
import { Modal, Platform, Pressable, Text, View } from "react-native";
import DateTimePicker from "@react-native-community/datetimepicker";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import type { Language } from "../../contracts/account";
import { historyDateLabel } from "../../shared/history";
import { calendarDate, calendarDay } from "./calendar-date";
import { Button, colors, styles } from "./ui";

export function DateField({ label, value, language, onChange }: { label: string; value: string; language: Language; onChange: (value: string) => void }) {
  const [open, setOpen] = useState(false), [draft, setDraft] = useState(() => calendarDate(value));
  const insets = useSafeAreaInsets();
  const text = value ? historyDateLabel(value, language) : language === "sq" ? "Zgjidh datën" : "Choose a date";
  const picker = <DateTimePicker value={draft} mode="date" display={Platform.OS === "ios" ? "spinner" : "default"} themeVariant="light" onChange={(event, date) => {
    if (Platform.OS === "android") { setOpen(false); if (event.type === "set" && date) onChange(calendarDay(date)); }
    else if (date) setDraft(date);
  }}/>;
  return <View style={styles.field}><Text style={styles.label}>{label}</Text>
    <Pressable accessibilityRole="button" accessibilityLabel={`${label}: ${text}`} style={styles.input} onPress={() => { setDraft(calendarDate(value)); setOpen(true); }}><Text style={[styles.body, { color: colors.ink }]}>{text}</Text></Pressable>
    {open && (Platform.OS === "android" ? picker : <Modal transparent animationType="slide" onRequestClose={() => setOpen(false)}>
      <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: "#0006" }}><View accessibilityViewIsModal style={{ backgroundColor: colors.white, padding: 20, paddingBottom: insets.bottom + 20, borderTopLeftRadius: 22, borderTopRightRadius: 22 }}>
        <Text style={[styles.label, { fontSize: 18 }]}>{label}</Text>{picker}
        <View style={styles.row}><Button title={language === "sq" ? "Anulo" : "Cancel"} secondary onPress={() => setOpen(false)}/><Button title={language === "sq" ? "Zgjidh" : "Choose"} onPress={() => { onChange(calendarDay(draft)); setOpen(false); }}/></View>
      </View></View>
    </Modal>)}
  </View>;
}
