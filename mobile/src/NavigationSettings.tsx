import { useEffect, useRef, useState } from "react";
import { Text, View } from "react-native";
import type { Language } from "../../contracts/account";
import type { MapProvider } from "../../shared/map-directions";
import { readMapProvider, saveMapProvider } from "./map-preference";
import { Card, Chip, Notice, styles } from "./ui";

export function NavigationSettings({ language }: { language: Language }) {
  const sq = language === "sq";
  const [provider, setProvider] = useState<MapProvider>("google");
  const [busy, setBusy] = useState(true), [error, setError] = useState(false), [saved, setSaved] = useState(false);
  const pending = useRef(false);
  useEffect(() => {
    let active = true;
    readMapProvider().then(value => { if (active) setProvider(value); }).catch(() => { if (active) setError(true); }).finally(() => { if (active) setBusy(false); });
    return () => { active = false; };
  }, []);
  async function choose(value: MapProvider) {
    if (pending.current || busy) return;
    pending.current = true; setBusy(true); setError(false); setSaved(false);
    try { await saveMapProvider(value); setProvider(value); setSaved(true); }
    catch { setError(true); }
    finally { pending.current = false; setBusy(false); }
  }
  return <Card>
    <Text style={styles.label}>{sq ? "Aplikacioni i navigimit" : "Navigation app"}</Text>
    <Text style={styles.body}>{sq ? "Zgjidh ku të hapet navigimi kur prek një pikë në hartë." : "Choose where directions open when you tap a station on the map."}</Text>
    <View style={styles.wrap}>{(["google", "apple"] as const).map(value => <Chip key={value} title={value === "apple" ? "Apple Maps" : "Google Maps"} selected={provider === value} disabled={busy} onPress={() => void choose(value)}/>)}</View>
    <Text style={styles.body}>{sq ? "Ruhet në këtë telefon. Parazgjedhja është Google Maps." : "Saved on this phone. Google Maps is the default."}</Text>
    <Notice success text={saved ? (sq ? "Zgjedhja u ruajt." : "Choice saved.") : ""}/>
    <Notice text={error ? (sq ? "Zgjedhja nuk u ruajt ose nuk u lexua. Provo përsëri." : "Could not save or read your choice. Please retry.") : ""}/>
  </Card>;
}
