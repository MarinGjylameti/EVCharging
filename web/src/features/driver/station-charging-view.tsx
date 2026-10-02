"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import type { Language, StationCharging } from "../../../../contracts/account";
import { useLiveSnapshot } from "./use-live-snapshot";
import { ChargingProgress } from "./charging-progress";
import { chargingLivePolicy } from "./live-update-policy";

export function StationChargingView({ initial, language }: { initial: StationCharging; language: Language }) {
  const sq = language === "sq";
  const stationId = initial.station.id;
  const url = `/api/driver/stations/${stationId}/charging`;
  const { snapshot, setSnapshot, connected, unavailable } = useLiveSnapshot(initial, url, stationId, chargingLivePolicy);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [now, setNow] = useState(0);
  const attempted = useRef(false);
  const mounted = useRef(false);
  const station = snapshot.station;
  const session = snapshot.connectionRequest && snapshot.session?.stoppedAt ? null : snapshot.session;
  const active = session && session.stoppedAt === null;
  const waiting = snapshot.connectionRequest !== null;
  const number = (value: number) => new Intl.NumberFormat(sq ? "sq-AL" : "en-GB", { maximumFractionDigits: 3 }).format(value);

  useEffect(() => {
    mounted.current = true;
    setNow(Date.now());
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => { mounted.current = false; clearInterval(timer); };
  }, []);

  const mutate = useCallback(async (method: "POST" | "DELETE") => {
    setBusy(true); setError("");
    try {
      const response = await fetch(url, { method });
      if (response.status === 401) { window.location.assign("/"); return; }
      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        throw new Error(data.code === "charger_in_use"
          ? (sq ? "Kjo pikë po përdoret nga një drejtues tjetër." : "This charger is in use by another driver.")
          : data.code === "driver_already_connected"
            ? (sq ? "Ke tashmë një karikim ose pritje në një pikë tjetër." : "You already have a charging session or waiting request at another charger.")
          : (sq ? "Lidhja nuk u konfirmua. Provo përsëri." : "Connection was not confirmed. Please retry."));
      }
      const data: StationCharging = await response.json();
      if (mounted.current) setSnapshot(data);
    } catch (failure) {
      if (mounted.current) setError(failure instanceof Error ? failure.message : (sq ? "Lidhja dështoi." : "Connection failed."));
    } finally { if (mounted.current) setBusy(false); }
  }, [url, sq, setSnapshot]);

  useEffect(() => {
    if (attempted.current) return;
    attempted.current = true;
    if (initial.canConnect && !initial.inUse && !initial.connectionRequest
      && !initial.session) void mutate("POST");
  }, [initial, mutate]);

  const requestExpired = waiting && now > Date.parse(snapshot.connectionRequest!.expiresAt);
  return <div className="driver-content charge-point-page">
    <nav className="charge-page-navigation" aria-label={sq ? "Navigimi i karikimit" : "Charging navigation"}>
      <Link href="/driver">← {sq ? "Kthehu te harta" : "Back to map"}</Link>
      <Link href="/driver/history">{sq ? "Historiku im" : "My history"} ↗</Link>
    </nav>
    <div className="charge-point-title"><div>
      <span className="driver-eyebrow">{sq ? "PIKA JOTE E KARIKIMIT" : "YOUR CHARGE POINT"}</span>
      <h1>{station.name}</h1><p>{station.address} · {station.city}</p>
    </div><span className={`charging-live ${connected && !unavailable ? "is-live" : ""}`} role="status"><i aria-hidden="true" />{connected && !unavailable ? (sq ? "Përditësime aktive" : "Live updates") : (sq ? "Po rilidhet…" : "Reconnecting…")}</span></div>
    <div className="charge-point-specs">
      <span><small>{sq ? "PIKA" : "POINT"}</small><strong>{station.code}</strong></span>
      <span><small>{sq ? "LIDHËSI" : "CONNECTOR"}</small><strong>{station.connectorType}</strong></span>
      <span><small>{sq ? "FUQIA E PIKËS" : "CHARGER RATING"}</small><strong>{number(station.powerKW)} kW</strong></span>
      <span><small>{sq ? "TARIFA" : "TARIFF"}</small><strong>{number(session?.unitPricePerKWh ?? station.pricePerKWh)} {session?.currency ?? station.currency} / kWh</strong></span>
    </div>
    {error && <div className="form-error" role="alert">{error}</div>}
    {snapshot.inUse && !active && <p className="charge-point-notice" role="status">{sq ? "Pika po përdoret nga një drejtues tjetër. Karikimi i tij nuk shfaqet në llogarinë tënde." : "Another driver is using this charger. Their session is private."}</p>}
    {session ? <ChargingProgress session={session} language={language} now={now || Date.parse(session.startReceivedAt ?? session.startedAt)} stale={unavailable} />
      : <section className="charge-waiting-card" aria-labelledby="charge-wait-title">
        <div className={`charge-wait-icon ${waiting && snapshot.deviceConnected && !requestExpired ? "is-ready" : ""}`} aria-hidden="true">ϟ</div>
        <span className="driver-eyebrow">{sq ? "LIDHJA ME PIKËN" : "CONNECTING TO THE CHARGER"}</span>
        <h2 id="charge-wait-title">{busy ? (sq ? "Po përgatis lidhjen…" : "Preparing connection…")
          : requestExpired ? (sq ? "Koha e pritjes përfundoi" : "Waiting time expired")
          : station.status === "OutOfService" ? (sq ? "Pika është jashtë shërbimit" : "Charger unavailable")
          : station.status === "Maintenance" ? (sq ? "Pika është në mirëmbajtje" : "Charger under maintenance")
          : waiting && snapshot.deviceConnected ? (sq ? "Gati për të nisur" : "Ready to start")
          : waiting ? (sq ? "Në pritje të pajisjes" : "Waiting for the charger")
          : (sq ? "Pika e karikimit u zgjodh" : "Charge point selected")}</h2>
        <p>{waiting && !requestExpired ? snapshot.deviceConnected
          ? (sq ? "Pajisja është lidhur. Sapo të nisë karikimi, matjet do të shfaqen këtu automatikisht." : "The charger is connected. Your measurements will appear here automatically when charging starts.")
          : (sq ? "Presim që pajisja të lidhet me shërbimin. Mund ta lësh këtë faqe hapur." : "Waiting for the charger to connect. You can leave this page open.")
          : (sq ? "Një sesion krijohet vetëm kur pajisja konfirmon nisjen e karikimit." : "A charging session is created only when the charger confirms the start.")}</p>
        <ol className="charge-connection-steps">
          <li className="is-done"><i aria-hidden="true">✓</i><span>{sq ? "Pika u zgjodh" : "Point selected"}</span></li>
          <li className={snapshot.deviceConnected ? "is-done" : ""}><i aria-hidden="true">{snapshot.deviceConnected ? "✓" : "2"}</i><span>{sq ? "Pajisja u lidh" : "Charger connected"}</span></li>
          <li><i aria-hidden="true">3</i><span>{sq ? "Nis karikimi" : "Charge starts"}</span></li>
        </ol>
        {snapshot.canConnect && <div className="charge-connect-actions">
          {waiting && !requestExpired ? <button type="button" className="button-outline" disabled={busy} onClick={() => void mutate("DELETE")}>{sq ? "Anulo pritjen" : "Cancel waiting"}</button>
            : <button type="button" className="driver-history-link" disabled={busy || snapshot.inUse} onClick={() => void mutate("POST")}>{sq ? "Lidhu me pikën" : "Connect to charger"}</button>}
        </div>}
      </section>}
    {session?.stoppedAt && snapshot.canConnect && !snapshot.inUse && <div className="charge-connect-actions">
      <button type="button" className="driver-history-link" disabled={busy} onClick={() => void mutate("POST")}>{sq ? "Përgatit karikim të ri" : "Prepare a new charge"} ↗</button>
    </div>}
    {snapshot.canConnect && !snapshot.inUse && !active && <p className="charge-test-instruction">{sq
      ? `Për testin aktual, QR anashkalohet. Lidh simulatorin ${station.code} dhe shtyp Start charging me TAG-001, lidhësi 1.`
      : `QR is bypassed for this test. Connect simulator ${station.code}, then press Start charging with TAG-001, connector 1.`}</p>}
    {!snapshot.canConnect && !session && <p className="charge-point-notice">{sq ? "Lidhja pa QR është e aktivizuar vetëm për pikat e testimit." : "Connection without QR is enabled only for test charge points."}</p>}
  </div>;
}
