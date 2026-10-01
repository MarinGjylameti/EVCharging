# Modeli i propozuar i databazës

Një PostgreSQL i dedikuar për EVCharging. Ky dokument është modeli i synuar për platformën e plotë. Katër migrimet e implementuara gjenden në backend/src/EVCharging.Infrastructure/Persistence/Migrations: identiteti/njoftimet/audit, charging_stations dhe charging_history për pamjen e Driver-it, versioni i pikës për konfliktet e redaktimit dhe fushat opsionale të vlerësimit të disponueshmërisë. [SQL për review](initial-schema.sql) është gjeneruar, por nuk është provuar në PostgreSQL. Tabelat e tjera më poshtë mbeten plan; refresh_tokens është zëvendësuar për këtë hap nga login_sessions me skadim absolut. Historia aktuale ruan një lidhje me pikën, energjinë dhe çmimin snapshot; nuk është ende sesion OCPP real.

## Tabelat dhe lidhjet

Tabelat e identitetit mund të përdorin primitives të ASP.NET Identity; emrat këtu shprehin modelin logjik dhe do të përshtaten me mappings. UUID për identitete, `timestamptz` për UTC, `numeric` për energji/çmime, `bigint version` për concurrency.

| Tabela | Fushat kryesore | Kufizimi / indeksi kryesor |
|---|---|---|
| users | id, normalized_email, password_hash, role, active, archived_at, security_version | Email unik; role vetëm DRIVER/ADMIN/SUPERADMIN |
| refresh_tokens | id, user_id, token_hash, family_id, expires_at, revoked_at, replacement_id | Hash unik; indeks user/expiry; FK pa humbje historie |
| driver_profiles | user_id, first_name, last_name, phone, vehicle_make/model/battery_type | Një profil për Driver; fushat opsionale pas konfirmimit |
| driver_settings | user_id, fushat e miratuara të preferencave | Një për përdorues; enum/fusha të validuara, jo JSON arbitrar |
| locations | id, name, address, latitude, longitude, archived_at, version | Koordinata në intervale; indeks archived_at |
| charge_points | id, identifier, name, serial_number, location_id, latitude, longitude, connector_type, rated_power_kw, reported_state, manual_restriction, last_observed_at, last_received_at, archived_at, version | ID publik unik edhe pas arkivimit; FK location RESTRICT; fuqi > 0 |
| location_assignments | admin_id, location_id, assigned_at, assigned_by | PK çift; FK; shkrim vetëm për Admin aktiv nga Superadmin |
| point_assignments | admin_id, point_id, assigned_at, assigned_by | PK çift; nuk jep akses automatik në pikat fqinje |
| price_revisions | id, point_id, revision, unit_price, currency, effective_at, created_at, actor_id | Unik point/revision; çmim >= 0; indeks point/effective_at/revision |
| charging_sessions | id, point_id, location_at_start_id, driver_id nullable, provider_id, external_session_id, state, connection/started/ended/disconnected timestamps, physical_closed_at nullable, energy_kwh, current_power_kw nullable, price_revision_id nullable, unit_price_snapshot nullable, currency nullable, calculated_cost nullable, calculation_version, time_quality, data_quality, version | Unik provider/external_session_id kur ekziston; indeks point/start dhe driver/start |
| session_measurements | id, session_id, source_event_id, observed_at, received_at, cumulative_energy_kwh, session_energy_kwh, power_kw nullable, quality | Unik source_event/measurement-key; indeks session/time |
| point_status_changes | id, point_id, source, actor_id nullable, old/new reported state, old/new restriction, old/new effective state, reason, observed_at, recorded_at | Indeks point/recorded_at; arsye e detyrueshme për manual |
| audit_entries | id, actor_id, actor_role_snapshot, action, entity_type/id, point_id/location_id nullable, before/after JSON të kufizuar, reason, recorded_at, trace_id | Indekse entity/time, point/time, actor/time; insert/read për runtime |
| notifications | id, recipient_id, source_event_id, type, point_id/session_id nullable, created_at | Unik recipient/source_event/type; indeks recipient/created_at |
| integration_point_bindings | provider_id, device_ref, connector_ref, point_id | Unik provider/device/connector; adapteri nuk lidh pajisje të panjohura automatikisht |
| integration_inbox | id, provider_id, event_id, payload_hash, normalized_type, external_session_id, device_sequence nullable, observed_at, received_at, processing_state, attempt_count, next_attempt_at, safe_failure_code | Unik provider/event_id; indeks processing_state/next_attempt_at |
| delivery_outbox | id, event_type, entity_id, entity_version, payload i kufizuar, created_at, sent_at, attempts, next_attempt_at | Indeks për të padërguarat; kufi retries/backoff |
| request_idempotency | actor_id, operation, key, payload_hash, result_resource_id, created_at, expires_at | Unik actor/operation/key; ruhet në transaksionin e ndryshimit |

Identiteti i pajisjes mund të mos japë eventId ose sessionId global. Adapteri duhet të dokumentojë çelësin e qëndrueshëm, me device/boot epoch/sequence/transaction ku duhen, para se të quhet i gatshëm. Tabela e bindings mbështet integrimin aktual dhe nuk sjell UI konfigurimi OCPP.

Snapshot-et e emrit/ID të pikës dhe location në sesion ruajnë kuptimin historik edhe pas riemërtimit. `driver_id` nullable mbulon eventet para identifikimit; një Driver lexon vetëm rreshtat me pronësi të verifikuar.

## Integriteti

- `charge_points.location_id`, referencat historike dhe çmimet përdorin `ON DELETE RESTRICT`. Heqjet e produktit bëhen me arkivim; runtime nuk fshin sesione/audit/price revisions.
- Një indeks unik parcial mbi point_id kur `physical_closed_at IS NULL` mbron një transaksion fizik të hapur për pikë logjike. Stop i verifikuar ose disconnect pa karikim e vendos këtë kohë; mungesa e matjes finale mund ta lërë sesionin në AwaitingFinalData, por nuk bllokon një transaksion të ri fizik. Nuk vendoset mbyllje artificiale për të anashkaluar constraint-in.
- `energy_kwh >= 0`, çmim/kosto jo negative; fundi nuk paraprin nisjen kur të dyja kohët njihen. Nuk shpiken kohë për të plotësuar një NOT NULL.
- Sesioni i finalizuar kërkon revisionin e çmimit, monedhën, energjinë dhe cost të vlefshme. Rreshti në pritje mund t’i ketë null, me data_quality përkatës.
- Propozim numeric: energji `numeric(18,6)`, çmim `numeric(18,6)`, kosto `numeric(20,2)`, fuqi `numeric(12,3)`. Kufijtë maksimalë dhe rounding verifikohen me të dhënat e pajisjes dhe politikën e biznesit.
- Revisionet e çmimit janë append-only. Çmimi aktual zgjidhet nga effective_at/revision; ndryshimi krijon revision të ri nën lock të pikës. Nuk ka dy fonte autoritative të çmimit.
- Audit before/after ruan vetëm fushat e ndryshuara të lejuara, jo password hash, token ose profile të plota. Actor role ruhet si snapshot për të mbijetuar ndryshimet e rolit.
- Roli i runtime DB nuk ka UPDATE/DELETE në audit dhe revisions. Roli i migrimeve është i veçantë. Kjo mbron nga gabime aplikative, pa pretenduar pandryshueshmëri ndaj administratorit të databazës.

## Transaksionet

**Ndryshim manual/çmimi:** lexim i burimit me version → autorizim mbi caktimet aktuale → kontroll i versionit dhe invariantave → update/revision → status history kur nevojitet → audit → outbox → commit. Audit ose constraints që dështojnë anulojnë të gjithë veprimin.

**Ngjarje pajisjeje:** autentikim provider → ruajtje durable në inbox/deduplikim → lock për pikën/sesionin → normalizim dhe tranzicion → matje/sesion/status/notification intents → mark processed dhe outbox → commit. Pranimi i eventit në inbox mund të jetë transaksion i veçantë nga përpunimi; një retry gjen inbox-in ekzistues. Nuk dërgohet SignalR për ndryshim ende të pakryer.

**Caktim/çaktivizim:** update caktimesh/llogarie → security/access version → revoke refresh sessions kur duhet → audit → event revokimi → commit. Dispatcher i realtime rivlerëson aksesin para dërgimit dhe evakuuon lidhjet/grupet e prekura.

Edhe dy administratorë dhe eventi i pajisjes përdorin të njëjtin version/lock discipline. Operacionet që prekin disa pika marrin locks në rend të qëndrueshëm. Retry për deadlock/concurrency është i kufizuar dhe vetëm mbi operacione të sigurta; konflikti i formularit kthehet te klienti, pa mbishkrim të heshtur.

## Query dhe evolucioni

Filtrat e sesioneve mbështeten nga indekse `(point_id, started_at, id)`, `(location_at_start_id, started_at, id)`, `(driver_id, started_at, id)`, si dhe indekse parciale për aktive. Listat përdorin limit maksimal dhe rend të qëndrueshëm; për histori preferohet cursor. Kërkesat e hartës përdorin viewport/koordinata dhe vetëm DTO të nevojshme. PostgreSQL pa PostGIS mjafton si pikënisje për bounding box; shtesa kërkon provë nevoje dhe miratim instalimi në fazën përkatëse.

Dashboard bën agregim mbi të njëjtin scope autorizimi. Energjia e përgjithshme përmbledh një total të vetëm për sesion, jo total sesioni plus matjet. Aktive/përfunduara trajtohen veç dhe të dhënat jo të plota etiketohen. Indekset dhe query-t testohen me vëllim përfaqësues përpara optimizimeve shtesë.

Çdo ndryshim skeme merr migrim EF të versionuar. Testohet krijimi nga zero dhe upgrade nga migrimi paraardhës në PostgreSQL të izoluar. CI/testet nuk përdorin HOUSS ose një lidhje production. Pas publikimit të mobile preferohen migrime additive; heqjet e fushave kërkojnë plan për klientët e vjetër. Afati i mbajtjes së inbox-it duhet të mbulojë dritaren e retry; çelësat e deduplikimit nuk pastrohen pa politikë të qartë.
