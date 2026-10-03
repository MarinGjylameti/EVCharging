import type { Language, Station } from "../../../../contracts/account";
import { remainingChargingMinutes } from "@/lib/availability";

export function AvailabilityCard({station,language,nowMs,stale}: {
  station: Station; language: Language; nowMs: number|null; stale: boolean;
}) {
  if (station.status!=="Charging") return null;
  const sq=language==="sq";
  const forecast=station.availabilityForecast;
  const remaining=nowMs===null || stale ? null : remainingChargingMinutes(forecast,nowMs);
  return <div className="station-forecast">
    <span className="driver-eyebrow">{forecast?.source==="Demo" ?
      (sq?"SHEMBULL DEMO":"DEMO EXAMPLE") : (sq?"KOHA E MBETUR":"TIME REMAINING")}</span>
    {remaining!==null && forecast ? <>
      <strong>{sq?`Rreth ${remaining} min të mbetura`:`About ${remaining} min remaining`}</strong>
      <small>{sq?`Lidhësi ${forecast.connectorId} · ${forecast.source==="Simulator"?"deri te objektivi i simulimit":forecast.source==="Calculated"?"deri në bateri të plotë":forecast.source==="Demo"?"shembull demo":"vlerësim nga pajisja"}`:
        `Connector ${forecast.connectorId} · ${forecast.source==="Simulator"?"until the simulation target":forecast.source==="Calculated"?"until the battery is full":forecast.source==="Demo"?"demo example":"device estimate"}`}</small>
      <small>{sq?"Koha është e përafërt; përfundimi i karikimit nuk garanton lirimin e lidhësit.":
        "Time is approximate; finishing the charge does not guarantee that the connector is free."}</small>
    </> : <p>{stale ? (sq?"Po rilidhet; në pritje të përditësimit të kohës së mbetur…":"Reconnecting; waiting for a remaining-time update…") : nowMs===null ? (sq?"Po kontrolloj kohën e mbetur…":"Checking remaining time…") : forecast ?
      (sq?"Vlerësimi i fundit ka skaduar. Duhet një përditësim i ri nga burimi i të dhënave.":
        "The latest estimate expired. A new update from the data source is needed.") :
      (sq?"Nuk ka ende një vlerësim të kohës së mbetur për këtë karikim.":
        "No remaining-time estimate is available for this charge yet.")}</p>}
  </div>;
}

