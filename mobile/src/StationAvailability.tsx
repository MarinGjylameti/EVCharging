import { useState } from "react";
import { Text, View } from "react-native";
import type { Language, Station } from "../../contracts/account";
import { remainingChargingMinutes } from "../../shared/availability";
import { useForegroundPoll } from "./use-foreground-poll";

export function StationAvailability({ station, language, stale }: { station: Station; language: Language; stale: boolean }) {
  const [now, setNow] = useState(Date.now);
  useForegroundPoll(async () => { setNow(Date.now()); }, 15000, station.status === "Charging" && !stale);
  if (station.status !== "Charging") return null;
  const sq = language === "sq", forecast = station.availabilityForecast;
  const remaining = stale ? null : remainingChargingMinutes(forecast, now);
  return <View style={{ padding: 14, gap: 6, borderRadius: 14, backgroundColor: "#edf5ff", borderWidth: 1, borderColor: "#cbdff8" }}>
    <Text style={{ fontSize: 11, fontWeight: "700", letterSpacing: .7, color: "#2c63a2" }}>{sq ? "KOHA E MBETUR" : "TIME REMAINING"}</Text>
    {remaining !== null && forecast ? <>
      <Text style={{ fontSize: 20, fontWeight: "700", color: "#1758ad" }}>{sq ? `Rreth ${remaining} min të mbetura` : `About ${remaining} min remaining`}</Text>
      <Text style={{ fontSize: 12, lineHeight: 18, color: "#48627e" }}>{sq ? "Lidhësi" : "Connector"} {forecast.connectorId} · {forecast.source === "Demo" ? (sq ? "shembull demo" : "demo example") : forecast.source === "Simulator" ? (sq ? "deri te objektivi i simulimit" : "until the simulation target") : forecast.source === "Calculated" ? (sq ? "deri në bateri të plotë" : "until the battery is full") : (sq ? "vlerësim nga pajisja" : "device estimate")}</Text>
      <Text style={{ fontSize: 12, lineHeight: 18, color: "#48627e" }}>{sq ? "Përfundimi i karikimit nuk garanton lirimin e lidhësit." : "Finishing the charge does not guarantee that the connector is free."}</Text>
    </> : <Text style={{ fontSize: 13, lineHeight: 19, color: "#48627e" }}>{stale ? (sq ? "Në pritje të rilidhjes dhe përditësimit të kohës." : "Waiting for reconnection and an updated estimate.") : (sq ? "Në pritje të një vlerësimi të ri nga karikuesi." : "Waiting for a fresh estimate from the charger.")}</Text>}
  </View>;
}
