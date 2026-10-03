"use client";

import type { ChargingCommandResult, ChargingDiagnostics, DriverChargingSession, Language } from "../../../../contracts/account";

function rejectionMessage(command: ChargingCommandResult, sq: boolean) {
  if (command.action === "StartTransaction" && command.failureCode === "OccurrenceConstraintViolation")
    return sq ? "Ekziston ende një transaksion aktiv. Përfundimi i animacionit në simulator nuk e mbyll atë; duhet konfirmimi i ndalimit nga pajisja." : "A transaction is still active. Finishing the simulator animation does not close it; the charger must confirm its stop.";
  if (command.failureCode === "SecurityError" || command.action === "Authorize")
    return sq ? "Kontrollo Tag ID: konfigurimi i këtij testi përdor TAG-001." : "Check the Tag ID: this test configuration uses TAG-001.";
  if (command.action === "StopTransaction")
    return sq ? "Kontrollo ID-në e transaksionit, regjistrin final të matësit në Wh dhe kohën e dërguar. Sesioni mbetet i hapur derisa ndalimi të pranohet." : "Check the transaction ID, final meter register in Wh and timestamp. The session remains open until the stop is accepted.";
  return sq ? "Pajisja dërgoi një mesazh që shërbimi nuk e pranoi. Detajet shfaqen më poshtë." : "The charger sent a message that the service rejected. Details appear below.";
}

export function SimulatorDiagnostics({ initial: data, session, language }: {
  initial: ChargingDiagnostics; session: DriverChargingSession | null; language: Language;
}) {
  const sq = language === "sq";
  const active = session?.stoppedAt === null;
  const samples = data.samples.length ? data.samples : data.lastCommand?.readings ?? session?.readings ?? [];
  const stopReason = session?.stopReason ?? data.stopReason;
  const command = data.lastCommand;
  const failure = data.lastFailure !== undefined ? data.lastFailure : command && !command.accepted ? command : null;
  return <section className="simulator-diagnostics">
    {failure && <div className="charge-point-notice" role="alert">
      <strong>{failure.action === "StartTransaction"
        ? (sq ? "Nisja e re nuk u pranua" : "The new start was rejected")
        : failure.action === "StopTransaction" ? (sq ? "Ndalimi nuk u pranua" : "The stop was rejected")
        : (sq ? `${failure.action} nuk u pranua` : `${failure.action} was rejected`)}</strong>
      <p>{rejectionMessage(failure, sq)}</p>
      <p>{failure.failureCode}{failure.description ? ` · ${failure.description}` : ""}</p>
    </div>}
    <details className="charge-reading-details">
      <summary>{sq ? "Diagnostikimi i simulatorit" : "Simulator diagnostics"}</summary>
      {data.externalSessionId && <p><strong>{sq ? "Transaction ID i këtij sesioni" : "This session’s Transaction ID"}: {data.externalSessionId}</strong>
        {active && <span> · {sq ? "Përdor këtë ID te StopTransaction dhe prit përgjigjen RESP." : "Use this ID in StopTransaction and wait for the RESP acknowledgement."}</span>}</p>}
      {data.meterStartWh !== null && <p>Meter start: {data.meterStartWh} Wh. {sq ? "Meter stop duhet të jetë regjistri final i matësit në Wh, jo objektivi në kWh." : "Meter stop must be the final meter register in Wh, not the target in kWh."}</p>}
      {stopReason && <p>{sq ? "Arsyeja e ndalimit" : "Stop reason"}: {stopReason}</p>}
      {command && <p role="status">{command.action}: {command.accepted ? (sq ? "U pranua" : "Accepted") : (sq ? "U refuzua" : "Rejected")} · {new Date(command.receivedAt).toLocaleTimeString(sq ? "sq-AL" : "en-GB", { timeZone: "Europe/Tirane", hour: "2-digit", minute: "2-digit", second: "2-digit" })}</p>}
      <p>{sq ? "Për fuqi: MeterValues → Power.Active.Import, Unit kW ose W. Për baterinë: SoC, Unit Percent. Për temperaturë: Temperature, Unit Celsius, Fahrenheit ose K. Një vlerë në Wh është energji." : "For power: MeterValues → Power.Active.Import, Unit kW or W. For battery: SoC, Unit Percent. For temperature: Temperature, Unit Celsius, Fahrenheit or K. A Wh value is energy."}</p>
      <p>{sq ? "Matjet e fundit të pranuara nga lidhësi përditësohen drejtpërdrejt. Vlerat bruto mund të ruhen pa u përdorur në përmbledhje." : "Latest accepted samples from the connector update live. Raw values may be stored without being used in the summary."}</p>
      {samples.length > 0 ? <div className="charge-samples-scroll" tabIndex={0} role="region" aria-label={sq ? "Tabela e matjeve nga simulatori" : "Simulator samples table"}><table className="charge-samples-table"><caption>{sq ? "Matjet nga simulatori" : "Simulator samples"}</caption>
        <thead><tr>{["Measurand", sq ? "Vlera" : "Value", "Phase", "Location", "Context", "Format", sq ? "Ora e pajisjes" : "Device time"].map(label => <th key={label} scope="col">{label}</th>)}</tr></thead>
        <tbody>{samples.map((sample, index) => <tr key={`${sample.receivedAt}-${index}`}>
          <td>{sample.measurand}</td><td>{sample.value} {sample.unit ?? "(default)"}</td><td>{sample.phase ?? "Total"}</td><td>{sample.location ?? "Outlet"}</td><td>{sample.context ?? "Sample.Periodic"}</td><td>{sample.format ?? "Raw"}</td><td>{sample.observedAt}</td>
        </tr>)}</tbody></table></div> : <p>{sq ? "Ende nuk ka matje për këtë sesion." : "No samples for this session yet."}</p>}
    </details>
  </section>;
}
