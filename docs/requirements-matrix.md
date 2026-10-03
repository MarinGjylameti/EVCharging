# Matrica e kërkesave dhe përgjegjësive

**Kontrolli aktual, 3 tetor 2026:** shih [krahasimin me kodin](client-requirements-audit.md). Gjendjet në këtë matricë pasqyrojnë hapa më të hershëm dhe nuk përbëjnë konfirmim të përfundimit të Fazës 1.

Burimi funksional është [specifikimi Word i nxjerrë](reference/mvp-source.txt). `§` është seksioni i dokumentit, `P` numri i paragrafit në XML, përfshirë paragrafët bosh. Brief-i shoqërues përcakton mënyrën e punës dhe kufijtë teknikë. Propozimi QR vjen nga mesazhi pasues i përdoruesit, jo nga Word-i.

**Gjendja e përgjithshme: implementimi ka nisur; backend dhe web kanë përparësi.** `Partial` = pjesë e kërkesës në kod, me kufij në kolonën e fundit; `Domain tested` = vetëm rregulla domeni, jo rrjedhë fund më fund; `API tested / web built` = API e testuar in-process dhe web i kompiluar, pa provë vizuale. `Planned`, `Clarify`, `Proposed` dhe `Excluded` ruajnë kuptimin e tyre. Shih [evidencën konkrete](implementation-status.md).

## Web kundrejt Mobile

| Përgjegjësia | Driver mobile | Admin web | Superadmin web | Backend |
|---|---|---|---|---|
| Autentikimi | Llogaria personale | Llogaria administrative | Llogaria kryesore | Kontroll kredencialesh, llogarie aktive dhe rolesh |
| Harta dhe detajet | Të gjitha pikat publike aktive | Burimet e caktuara | Të gjitha | DTO publike pa të dhëna drejtuesi |
| Karikimi aktual dhe historiku | Vetëm sesionet e veta | Pikat e lejuara | Të gjitha | Lidhje e verifikuar me drejtuesin |
| Profili/automjeti/cilësimet | Po, sipas saktësimit | Vetëm hyrje/dalje e nevojshme | Vetëm hyrje/dalje e nevojshme | Ownership dhe validim |
| Shtim/redaktim/arkivim pike | Jo | Sipas caktimeve | Global | Autorizim, versionim dhe audit |
| Gjendja manuale/çmimet | Vetëm shikim | Sipas caktimeve | Global | Histori dhe snapshot sesioni |
| Paneli operativ | Jo | Për burimet e lejuara | Në nivel sistemi | Agregime serveri |
| Vendndodhjet/administratorët/caktimet | Jo | Shikim vendndodhjesh të lejuara | Menaxhim | Politika Superadmin |
| Lista e drejtuesve | Jo | Vetëm identiteti i nevojshëm në sesion | Pamje globale | PII e kufizuar |
| Njoftimet | Vetëm personale, nëse konfirmohen | Operative sipas caktimeve | Globale | Lloje dhe marrës të kufizuar |
| Audit | Jo | Sipas burimeve të lejuara | Global | Histori e pandryshueshme |
| QR, sipas mesazhit pasues | Rrjedhë e propozuar | Pa faqe shtesë | Pa faqe shtesë | Zgjidhje e kodit te pika; verifikim sesioni |

Kjo ndarje vjen nga brief-i: mobile për Driver dhe web për administrim. Kërkesa e fundit e përdoruesit shtoi një portal web Driver për provë, me hartë dhe historik demo, ndërsa versioni mobile pret stabilizimin e web-it. Harta operative dhe sesionet reale kërkojnë integrimin e ardhshëm me karikuesit.

## Kërkesat funksionale

| ID | Burimi | Kërkesa dhe fusha konkrete | Moduli / pamja | Gjendja | Pranimi i planifikuar |
|---|---|---|---|---|---|
| R01 | §1 P003–008; §15 | Tre role: DRIVER, ADMIN, SUPERADMIN; monitorim në kohë reale | Identity, API, realtime | Partial | Role/API të testuara; scope pikash dhe realtime ende Planned. |
| R02 | §1 P009; §7 | Shfaqje dhe llogaritje kostoje pa pagesë elektronike | Pricing, Sessions | Domain tested | ChargingSession teston 20 × 0.35 = 7; persistenca/UI e kostos ende Planned. |
| R03 | §2 P014–021 | Harta: vendndodhje, emër/ID, gjendje, disponueshmëri, lidhës, fuqi, çmim | Points, Mobile Map | Planned | Pikat aktive shfaqen; asnjë humbje e heshtur nga kufiri i marker-ëve |
| R04 | §2 P022–027 | Pesë gjendje dhe ngjyrat: available, occupied, charging, out of service, maintenance | Points, kontrata, UI | Domain tested | Gjendjet dhe mapping OCPP janë të testuara; rrjeti/UI ende Planned. |
| R05 | §2 P029–038 | Detaji: emër, ID, vendndodhje, gjendje, lidhës, fuqi, çmim/kWh, disponueshmëri | Points, Mobile Detail | Planned | Detaji dhe harta përputhen pas eventit |
| R06 | §2 P041–043 | Zbulimi i lidhjes, ora e lidhjes dhe fillimi i karikimit | Integration, Sessions | Planned | Event i pajisjes krijon/update-on lidhjen pa dyfishim |
| R07 | §2 P044–048 | Gjendja, kWh, fuqi aktuale nëse jepet, kohëzgjatje, kosto gjatë karikimit | Sessions, Mobile Active | Planned | Fuqia e munguar shfaqet e panjohur; kosto nga serveri |
| R08 | §2 P049–053 | Në fund: ora, kohëzgjatja, energjia dhe kostoja totale | Sessions | Domain tested | Përfundimi/energjia/kostoja testohen në domain; përmbledhja UI ende Planned. |
| R09 | §15 P254 | Historiku personal i karikimeve | Sessions, Mobile History | Planned | Vetëm sesionet e drejtuesit; paginim dhe detaj |
| R10 | §3 P055–061 | Profili me emër, mbiemër, email, telefon | Profile | Partial | API dhe web për profilin personal; fushat e tjera të produktit mbeten sipas saktësimit. |
| R11 | §3 P062–064 | Makina: markë, model, lloj baterie | Profile | API implemented | Fusha opsionale të automjetit në API; mobile i shtyrë. |
| R12 | §3 P065–066; §4 P079–080 | Ndryshim fjalëkalimi dhe dalje | Identity, Mobile Account | API tested / web built | Password current verifikohet; revokim i të gjitha sesioneve i testuar. Rrjedha web kompilohet. |
| R13 | §4 P073 | Gjuha e aplikacionit | Settings, i18n | Partial | Sq/en konfirmuar; API ruan gjuhën, web kompilohet; mobile i shtyrë. |
| R14 | §4 P074 | Cilësime njoftimesh | Settings, Notifications | Partial | Preferencë push/API dhe regjistrim pajisjeje; dërgimi real ende i patestuar. |
| R15 | §4 P075 | Cilësimet e hartës | Settings, Map | Clarify | Pa satellite/offline/navigation të supozuar |
| R16 | §4 P076 | Njësitë e matjes | Settings | Clarify | kW/kWh ruhen si njësi autoritative; zgjedhjet UI të papërcaktuara |
| R17 | §4 P077–078 | Privatësia dhe siguria | Settings, Identity | Clarify | Përmbajtja kërkon saktësim; pa shpikur 2FA/eksporte |
| R18 | §5 P083; §15 P259 | Admin vetëm për pikat e caktuara | Access | Planned | Query, write dhe realtime përdorin të njëjtën politikë |
| R19 | §5 P084–094 | Shtim: emër, ID, serial, vendndodhje, koordinata, lidhës, fuqi, gjendje, çmim/kWh | Points, Pricing | Planned | Admin krijon vetëm në vendndodhje ku ka leje krijimi |
| R20 | §5 P095–103 | Ndryshim emri, vendndodhjeje, të dhënash, lidhësi, fuqie, çmimi dhe gjendjeje | Points, Pricing | Planned | Versioni i vjetër jep konflikt; kontroll i burimit/destinacionit |
| R21 | §5 P104–106 | Heqje përmes arkivimit, ruajtje historie | Points | Planned | Nuk fshihen sesione, çmime ose audit; pika hiqet nga zbulimi |
| R22 | §6 P109–113 | Admin/Superadmin ndryshon manualisht gjendjen | Points | Domain tested | Mirëmbajtja nuk mbishkruhet nga raportimi; API/UI operative ende Planned. |
| R23 | §6 P114–119 | Ndryshim manual ruan actor, gjendjen para/pas, datë/orë, arsye | Audit, Points | Planned | Arsye e detyrueshme, audit në të njëjtin transaksion |
| R24 | §7 P122–130 | Çmim për çdo pikë, model për kWh, shembulli i kostos | Pricing | Domain tested | Decimal dhe snapshot i çmimit në domain; menaxhimi i tarifave ende Planned. |
| R25 | §7 P131–135 | Vendosje, ndryshim, çmim aktual dhe histori çmimesh | Pricing, Web Point | Planned | Çmimi i ri nuk ndryshon sesionet ekzistuese |
| R26 | §7 P136 | Çdo ndryshim çmimi regjistrohet në historik | Audit, Pricing | Planned | PriceRevision dhe audit ruhet atomikisht |
| R27 | §8 P139–146 | Panel: total pika dhe numër sipas pesë gjendjeve | Dashboard | Planned | Totalet serveri respektojnë caktimet |
| R28 | §8 P147–150 | Karikime aktive/përfunduara, energji totale, çmime | Dashboard | Planned | Pa numërim të dyfishtë të matjeve/sesioneve |
| R29 | §8 P153–156 | Aktivitetet: fillim/fund, lidhje/shkëputje automjeti | Sessions, activity projection | Planned | Ngjarje të deduplikuara, kohë dhe burim të dukshëm |
| R30 | §8 P157–159 | Aktivitetet: jashtë funksionit, gjendje e ndryshuar, çmim i ndryshuar | Points, Pricing, Audit | Planned | Aktiviteti vjen nga eventet e ruajtura |
| R31 | §9 P162 | Admin sheh karikime aktive dhe të përfunduara | Sessions, Web | Planned | Vetëm pikat e lejuara |
| R32 | §9 P164–173 | Sesioni: ID, pikë, drejtues, fillim/fund, kohëzgjatje, kWh, gjendje, çmim/kWh, kosto | Sessions | Planned | DTO serveri ruan identitetin dhe snapshot historik |
| R33 | §9 P174–179 | Filtra sipas pikës, vendndodhjes, gjendjes, datës, drejtuesit | Sessions query | Planned | Filter para paginimit; intervale kohore të përcaktuara |
| R34 | §10 P182–188 | Superadmin: total pika, vendndodhje, administratorë, drejtues | Dashboard | Planned | Agregime globale me definicione aktive/arkivuara |
| R35 | §10 P189–194 | Pika aktive/jashtë funksionit/mirëmbajtje, karikime aktive, energji, aktivitet | Dashboard | Planned | “Pika aktive” përkufizohet në politikën e raportimit |
| R36 | §11 P198 | Shtim administratori | Identity | Planned | Vetëm Superadmin; email unik; pa kredenciale në log |
| R37 | §11 P199 | Redaktim të dhënash administratori | Identity | Planned | Input i kufizuar dhe audit |
| R38 | §11 P200 | Aktivizim/çaktivizim administratori | Identity, Access | Planned | Aksesi ndalet edhe me token të vlefshëm dhe lidhje të hapur |
| R39 | §11 P201 | Heqje administratori | Identity | Planned | Arkivim/revokim, actor historik mbetet i lexueshëm |
| R40 | §11 P202 | Caktim te vendndodhja | Access | Planned | Semantika e caktimit përshkruhet shprehimisht |
| R41 | §11 P203 | Caktim pikash konkrete | Access | Planned | Admin me pikë të caktuar nuk fiton gjithë vendndodhjen |
| R42 | §11 P204–208 | Të drejtat e aksesit, Superadmin global | Access | Planned | Tre role fikse; pa role dinamike të shpikura |
| R43 | §12 P212 | Shtim vendndodhjeje | Locations | Planned | Vetëm Superadmin, emër dhe koordinata të vlefshme |
| R44 | §12 P213 | Ndryshim vendndodhjeje | Locations | Planned | Concurrency dhe audit |
| R45 | §12 P214 | Arkivim vendndodhjeje | Locations | Planned | Pa cascade delete; trajtohen më parë pikat aktive |
| R46 | §12 P215–216 | Adresë dhe koordinata | Locations, Web Map | Planned | Latitude/longitude të vlefshme; pa geocoding të detyrueshëm |
| R47 | §12 P217–219 | Caktim dhe listim pikash brenda vendndodhjes; një me shumë | Locations, Points | Planned | FK dhe kontroll i zhvendosjes |
| R48 | §13 P223 | Njoftim për pikë jashtë funksionit | Notifications | Partial | Tekst sq/en dhe queue të implementuara; eventet/marrësit realë dhe push në pajisje ende të patestuara. |
| R49 | §13 P224 | Njoftim për defekt | Notifications | Partial | Tekst sq/en dhe queue të implementuara; eventet/marrësit realë dhe push në pajisje ende të patestuara. |
| R50 | §13 P225 | Njoftim për fillim karikimi | Notifications | Partial | Tekst sq/en dhe queue të implementuara; eventet/marrësit realë dhe push në pajisje ende të patestuara. |
| R51 | §13 P226 | Njoftim për përfundim karikimi | Notifications | Partial | Tekst sq/en dhe queue të implementuara; eventet/marrësit realë dhe push në pajisje ende të patestuara. |
| R52 | §13 P227 | Njoftim për shkëputje të papritur | Notifications | Partial | Tekst sq/en dhe queue të implementuara; eventet/marrësit realë dhe push në pajisje ende të patestuara. |
| R53 | §13 P228 | Njoftim për ndryshim manual gjendjeje | Notifications | Partial | Tekst sq/en dhe queue të implementuara; eventet/marrësit realë dhe push në pajisje ende të patestuara. |
| R54 | §13 P229 | Njoftim për ndryshim çmimi | Notifications | Partial | Tekst sq/en dhe queue të implementuara; eventet/marrësit realë dhe push në pajisje ende të patestuara. |
| R55 | §14 P232–240 | Audit: përdorues, rol, veprim, pikë/vendndodhje, para/pas, datë/orë | Audit | Partial | Audit append-only në EF dhe ndryshimet identity; modulet operative/DB grants ende Planned. |
| R56 | §15 P266–267 | Admin sheh njoftimet dhe historikun e veprimeve | Notifications, Audit | Planned | Filtrimi në DB për burimet e autorizuara |
| R57 | §15 P270–279 | Superadmin ka akses global në pika, vendndodhje, adminë, drejtues, sesione, çmime, akses, njoftime, audit | Identity dhe modulet | Planned | Pamje globale; CRUD i drejtuesve nuk supozohet nga fjala “akses” |
| R58 | §16 P283–300 | Përmbledhje e funksioneve të Fazës 1 | R01–R57 | Planned | Pranim end-to-end sipas fazave; jo vetëm ekranet |
| R59 | §3 P067–068; §16 P302–306 | Banka, pagesa elektronike, portofol, faturim elektronik, abonime | Asnjë modul | Excluded | Pa tabela, API, faqe ose workflow pagesash |
| R60 | §16 P307 | Pagesat e ardhshme pa rindërtuar platformën | Kufiri Sessions/Pricing | Planned | Snapshot dhe kontrata; pa kod spekulativ pagese |
| R61 | Brief §8/§25; profili/fjalëkalimi në Word | Hyrje dhe lifecycle i sesionit të përdoruesit | Identity | API tested / web built | Driver regjistrohet vetë; hyrje/dalje, token opaque, cookie web dhe revokim. Pa refresh rotation. |
| R62 | Brief §37 | Vetëm discovery/arkitekturë në detyrën e parë | Dokumentacion | Delivered | Dokumente dhe strukturë e re; pa funksionalitet produkti |
| U01 | Mesazhi pasues i përdoruesit, 2026-09-28 | Drejtuesi mund të skanojë QR te karikuesi për t’u lidhur me pikën | Mobile QR, Integration | Proposed | QR identifikon pikën; pronësia e sesionit kërkon evidencë shtesë |
| U02 | Konfirmim i përdoruesit, 2026-09-28 | Drejtuesi regjistrohet vetë | Identity | API tested | Signup publik cakton vetëm Driver; UI mobile i shtyrë |
| U03 | Konfirmim i përdoruesit, 2026-09-28 | Push në telefon, sq/en sipas gjuhës së zgjedhur | Notifications | Partial | Tekste, preferencë, queue/transport të implementuara; pa dërgim real |
| U04 | Prioriteti i fundit i përdoruesit, 2026-09-28 | Stabilizimi web përpara mobile | Plani i punës | Applied | Mobile i shtyrë; backend/web në fokus |

## Politika që nuk përcaktohen nga dokumenti

“Mund të përfshijë” në Profil/Cilësime nuk përkthehet automatikisht në detyrim për çdo fushë ose faqe. Seksionet rezervohen në navigim, ndërsa opsionet e tyre konfirmohen para implementimit. Seksioni 13 është përfshirë sepse përmbledhja e Fazës 1 i përmend njoftimet shprehimisht; kanalet dhe marrësit mbeten të hapur.

Monedha, rrumbullakimi, momenti i fiksimit të çmimit, gjuhët, afatet e ruajtjes, caktimi pas zhvendosjes së pikës dhe regjistrimi i drejtuesit shënohen në [regjistrin e paqartësive](implementation-plan.md). Nuk futen fusha për pagesa, karta RFID, abonime ose akses OCPP në UI vetëm sepse shfaqen në fotografi.
