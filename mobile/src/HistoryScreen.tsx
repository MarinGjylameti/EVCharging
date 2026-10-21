import { useRef, useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import type { Language } from "../../contracts/account";
import { defaultHistoryFilters, historyDateLabel, historyDay, historyScale, historyRange, type HistoryFilters, type HistoryMetric, type HistoryPeriod } from "../../shared/history";
import { useHistoryPage } from "./use-history-page";
import { emptyHistoryPage } from "../../shared/history-query";
import { messages } from "./i18n";
import { Button, Card, ChoiceField, Chip, Notice, Screen, colors, styles } from "./ui";
import { DateField } from "./DateField";

export function HistoryScreen({ token, language }: { token: string; language: Language }) {
  const t = messages[language]; const sq = language === "sq";
  const [filters, setFilters] = useState<HistoryFilters>(defaultHistoryFilters);
  const [metric, setMetric] = useState<HistoryMetric>("energy");
  const [selectedKey, setSelectedKey] = useState("");
  const [pageNumber, setPageNumber] = useState(1);
  const [selectedId, setSelectedId] = useState("");
  const result = useHistoryPage(token, filters, pageNumber, selectedKey);
  const metadata = useRef(emptyHistoryPage());
  if (result.data) metadata.current = result.data;
  const { stations, currencies, bounds } = metadata.current;
  const page = result.data ?? emptyHistoryPage();
  const { range, chart, summary } = page;
  const bucket = chart.buckets.find(item => item.key === selectedKey);
  const currency = filters.currency || (currencies.includes("EUR") ? "EUR" : currencies[0] ?? "EUR");
  const number = (value: number) => new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { maximumFractionDigits: 2 }).format(value);
  const value = (item: typeof chart.buckets[number]) => metric === "energy" ? item.energyKWh : metric === "charges" ? item.charges : item.costs[currency] ?? 0;
  const unit = metric === "energy" ? "kWh" : metric === "cost" ? currency : t.charges;
  const scale = historyScale(chart.buckets.map(value));
  const label = (from: string, until = from) => chart.granularity === "month" ? `${historyDateLabel(from, language, "month")}${from.slice(0, 7) !== until.slice(0, 7) ? ` – ${historyDateLabel(until, language, "month")}` : ""}`
    : `${historyDateLabel(from, language)}${until !== from ? ` – ${historyDateLabel(until, language)}` : ""}`;
  function select(key: string) { setSelectedKey(key); setPageNumber(1); setSelectedId(""); }
  function change(next: HistoryFilters) { setFilters(next); select(""); if (!next.currency && currencies.length > 1 && metric === "cost") setMetric("energy"); }
  function period(next: HistoryPeriod) { change({ ...filters, period: next, from: filters.from || range?.from || "", until: filters.until || range?.until || "" }); }
  const date = (timestamp: string) => { const day = historyDay(timestamp); return day ? `${historyDateLabel(day, language)} · ${new Intl.DateTimeFormat(sq ? "sq-AL" : "en-GB", { hour: "2-digit", minute: "2-digit", timeZone: "Europe/Tirane", hourCycle: "h23" }).format(new Date(timestamp))}` : "—"; };
  return <Screen language={language} refresh={result.reload}>
    <Text style={styles.eyebrow}>{t.myActivity.toUpperCase()}</Text><Text style={styles.title}>{t.history}</Text>
    {page.hasDemo && <Text style={styles.tag}>{t.demoHistory}</Text>}
    {result.pending && <Text accessibilityLiveRegion="polite" style={styles.body}>{t.loading}</Text>}
    {result.error && <><Notice text={sq ? "Historiku nuk u rifreskua. Kontrollo datat dhe riprovo." : "History could not refresh. Check the dates and retry."}/>{result.data && <Text style={styles.body}>{sq ? "Po shfaqen të dhënat e fundit." : "Showing the last loaded data."}</Text>}<Button title={t.retry} onPress={() => void result.reload()}/></>}
    {!bounds ? !result.pending && !result.error && <Card><Text style={styles.body}>{t.noHistory}</Text></Card> : <>
      <Card><Text style={[styles.label, { fontSize: 17 }]}>{sq ? "Filtro historikun" : "Filter history"}</Text>
        <View style={styles.wrap}>{(["7d", "30d", "6m", "custom"] as const).map(item => <Chip key={item} title={item === "7d" ? (sq ? "7 ditë" : "7 days") : item === "30d" ? (sq ? "30 ditë" : "30 days") : item === "6m" ? (sq ? "6 muaj" : "6 months") : (sq ? "Datat" : "Dates")} selected={filters.period === item} onPress={() => period(item)}/>)}</View>
        {filters.period === "custom" && <><DateField label={sq ? "Nga data" : "From date"} value={filters.from} onChange={from => change({ ...filters, from })} language={language}/><DateField label={sq ? "Deri më" : "Until date"} value={filters.until} onChange={until => change({ ...filters, until })} language={language}/></>}
        <ChoiceField label={sq ? "Pika" : "Station"} value={filters.stationId} options={[{ value: "", label: sq ? "Të gjitha pikat" : "All stations" }, ...stations.map(item => ({ value: item.stationId, label: item.stationName }))]} onChange={stationId => change({ ...filters, stationId })} language={language}/>
        <ChoiceField label={sq ? "Energjia për seancë" : "Energy per session"} value={filters.energy} options={[{ value: "all", label: sq ? "Çdo sasi" : "Any amount" }, { value: "small", label: "≤ 10 kWh" }, { value: "medium", label: "> 10–30 kWh" }, { value: "large", label: "> 30 kWh" }]} onChange={energy => change({ ...filters, energy: energy as HistoryFilters["energy"] })} language={language}/>
        {currencies.length > 1 && <ChoiceField label={sq ? "Monedha" : "Currency"} value={filters.currency} options={[{ value: "", label: sq ? "Të gjitha monedhat" : "All currencies" }, ...currencies.map(code => ({ value: code, label: code }))]} onChange={code => change({ ...filters, currency: code })} language={language}/>}
        <Button title={sq ? "Rivendos filtrat" : "Reset filters"} secondary onPress={() => { change(defaultHistoryFilters); setMetric("energy"); }}/>
        <Notice text={filters.period === "custom" && !historyRange([], filters) ? (sq ? "Plotëso datat dhe vendos datën e fundit pas ose njësoj me të parën." : "Enter valid dates with the end on or after the start.") : ""}/>
      </Card>
      {result.data && <>
      {range && <Text style={styles.body}>{historyDateLabel(bucket?.from ?? range.from, language)} – {historyDateLabel(bucket?.until ?? range.until, language)}</Text>}
      <View style={[styles.row, { alignItems: "stretch" }]}><View style={[styles.card, { flex: 1, padding: 14 }]}><Text style={styles.label}>{t.charges}</Text><Text style={styles.metric}>{summary.charges}</Text></View><View style={[styles.card, { flex: 1, padding: 14 }]}><Text style={styles.label}>{t.energy}</Text><Text style={styles.metric}>{number(summary.energyKWh)}</Text><Text style={styles.body}>kWh</Text></View></View>
      <Card><Text style={styles.label}>{t.calculatedCost}</Text>{Object.entries(summary.costs).map(([code, total]) => <Text key={code} style={styles.metric}>{number(total)} {code}</Text>)}{!page.total && <Text style={styles.metric}>0 {currency}</Text>}</Card>
      <Card><Text style={[styles.label, { fontSize: 18 }]}>{sq ? "Karikimet në kohë" : "Charging over time"}</Text>
        <View style={styles.wrap}>{(["energy", "cost", "charges"] as const).map(item => <Chip key={item} title={item === "energy" ? t.energy : item === "cost" ? (sq ? "Kostoja" : "Cost") : t.charges} selected={metric === item} onPress={() => { if (item === "cost" && !filters.currency && currencies.length > 1) change({ ...filters, currency }); setMetric(item); }}/>)}</View>
        <Text style={styles.body}>{unit} · {sq ? "Prek një kolonë për seancat e saj." : "Tap a bar for its sessions."}</Text>
        {chart.buckets.some(item => item.charges > 0) ? <View style={{ flexDirection: "row", gap: 5 }}>
          <View style={{ width: 38, height: 180, justifyContent: "space-between" }}>{[...scale.ticks].reverse().map(tick => <Text key={tick} style={{ color: colors.muted, fontSize: 10 }}>{number(tick)}</Text>)}</View>
          <ScrollView horizontal showsHorizontalScrollIndicator contentContainerStyle={{ gap: 5, paddingHorizontal: 4 }}>
            {chart.buckets.map(item => <Pressable key={item.key} accessibilityRole="button" accessibilityState={{ selected: selectedKey === item.key }} accessibilityLabel={`${label(item.from, item.until)}: ${number(value(item))} ${unit}, ${item.charges} ${t.charges}`} onPress={() => select(selectedKey === item.key ? "" : item.key)} style={{ width: 44, alignItems: "center", gap: 9 }}>
              <View style={{ height: 180, width: "100%", justifyContent: "flex-end", alignItems: "center", borderBottomWidth: 1, borderBottomColor: colors.muted }}><View style={{ height: Math.max(0, value(item)) / scale.max * 180, width: 25, backgroundColor: selectedKey === item.key ? colors.green : "#719b85", borderTopLeftRadius: 5, borderTopRightRadius: 5 }}/></View>
              <Text style={{ fontSize: 10, color: colors.ink }}>{chart.granularity === "month" ? historyDateLabel(item.from, language, "month", true) : new Date(item.date).getUTCDate()}</Text>
            </Pressable>)}
          </ScrollView>
        </View> : <Text style={styles.body}>{sq ? "Asnjë seancë për këta filtra." : "No sessions match these filters."}</Text>}
        {bucket && <><Text style={styles.label}>{label(bucket.from, bucket.until)} · {number(value(bucket))} {unit}</Text><Button title={sq ? "Shfaq gjithë periudhën" : "Show the whole period"} secondary onPress={() => select("")}/></>}
      </Card>
      <Text style={styles.body}>{sq ? "Filtrat përfshijnë të gjithë historikun tënd." : "Filters include your full history."}</Text>
      <Text style={styles.label}>{page.total ? `${page.offset + 1}–${page.offset + page.items.length} / ${page.total}` : "0"} {t.charges.toLowerCase()}</Text>
      {page.items.map(item => <Card key={item.id}><Pressable accessibilityRole="button" accessibilityState={{ expanded: selectedId === item.id }} onPress={() => setSelectedId(selectedId === item.id ? "" : item.id)} style={{ minHeight: 48, gap: 8 }}>
        <Text style={[styles.label, { fontSize: 16 }]}>{item.stationName}</Text><Text style={styles.body}>{date(item.endedAt)}</Text><Text style={styles.label}>{number(item.energyKWh)} kWh · {number(item.totalCost)} {item.currency}</Text>
      </Pressable>{selectedId === item.id && <View style={{ borderTopWidth: 1, borderTopColor: colors.border, paddingTop: 12, gap: 8 }}><Text style={styles.body}>{t.started}: {date(item.startedAt)}</Text><Text style={styles.body}>{t.finished}: {date(item.endedAt)}</Text><Text style={styles.body}>{t.pricePerKWh}: {number(item.unitPriceSnapshot)} {item.currency}</Text><Text style={styles.body}>{t.noPayment}</Text></View>}</Card>)}
      {!page.total && <Card><Text style={styles.body}>{sq ? "Ndrysho ditën ose zgjero filtrat." : "Choose another day or broaden the filters."}</Text></Card>}
      {page.pages > 1 && <View style={styles.row}><Button title={sq ? "← Para" : "← Previous"} secondary disabled={page.page === 1} onPress={() => { setPageNumber(page.page - 1); setSelectedId(""); }}/><Text style={styles.label}>{page.page}/{page.pages}</Text><Button title={sq ? "Tjetër →" : "Next →"} secondary disabled={page.page === page.pages} onPress={() => { setPageNumber(page.page + 1); setSelectedId(""); }}/></View>}
    </>}</>}
  </Screen>;
}
