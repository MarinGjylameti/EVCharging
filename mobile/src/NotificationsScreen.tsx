import { useState } from "react";
import { Text, View } from "react-native";
import type { Language, Notification } from "../../contracts/account";
import { historyDateLabel } from "../../shared/history";
import { notificationDay, notificationPage, notificationTime } from "./notification-data";
import { messages } from "./i18n";
import { Button, Card, ChoiceField, Screen, styles } from "./ui";

export function NotificationsScreen({ notifications, language, refresh }: { notifications: Notification[]; language: Language; refresh: () => Promise<void> }) {
  const sq = language === "sq", t = messages[language];
  const [day, setDay] = useState(""), [page, setPage] = useState(1);
  const result = notificationPage(notifications, day, page);
  const label = (value: string) => value === "unknown" ? (sq ? "Pa datë" : "Date unavailable") : historyDateLabel(value, language);
  return <Screen key={`${day}:${result.page}`} language={language} refresh={refresh}>
    <Text style={styles.title}>{t.notifications}</Text>
    <ChoiceField label={sq ? "Dita" : "Day"} value={day} language={language} onChange={value => { setDay(value); setPage(1); }} options={[
      { value: "", label: sq ? "Të gjitha ditët" : "All days" },
      ...[...new Set([...result.days, ...(day ? [day] : [])])].map(value => ({ value, label: label(value) })),
    ]}/>
    <Text style={styles.body}>{result.total} {t.notifications.toLowerCase()} · {sq ? "Ora e Shqipërisë (24 orë)" : "Albania time (24-hour)"}</Text>
    {result.items.length ? result.items.map((item, index) => <View key={item.id} style={{ gap: 10 }}>
      {(index === 0 || notificationDay(item.createdAt) !== notificationDay(result.items[index - 1].createdAt)) && <Text style={styles.label}>{label(notificationDay(item.createdAt))}</Text>}
      <Card><Text style={styles.label}>{item.text.title}</Text><Text style={styles.body}>{item.text.body}</Text><Text style={styles.body}>{notificationTime(item.createdAt)}</Text></Card>
    </View>) : <Card><Text style={styles.body}>{day ? (sq ? "Nuk ka njoftime për këtë ditë." : "No notifications for this day.") : t.emptyNotifications}</Text>{day && <Button title={sq ? "Shfaq të gjitha ditët" : "Show all days"} secondary onPress={() => { setDay(""); setPage(1); }}/>}</Card>}
    {result.pages > 1 && <View style={{ gap: 10 }}>
      <Text style={styles.body}>{sq ? "Faqja" : "Page"} {result.page} / {result.pages}</Text>
      <View style={styles.row}><View style={{ flex: 1 }}><Button title={sq ? "← Më të rejat" : "← Newer"} secondary disabled={result.page <= 1} onPress={() => setPage(result.page - 1)}/></View><View style={{ flex: 1 }}><Button title={sq ? "Më të vjetrat →" : "Older →"} secondary disabled={result.page >= result.pages} onPress={() => setPage(result.page + 1)}/></View></View>
    </View>}
  </Screen>;
}
