# Web — Driver, Admin dhe Superadmin

Next.js + TypeScript. Driver-i sheh hartën e pikave, gjendjen, filtrat dhe historikun e vet. Pas lejes së shfletuesit, harta qendërzohet automatikisht te vendndodhja e tij dhe rendit pikat sipas largësisë. Përzgjedhja e qytetit është opsionale dhe fillon bosh; dropdown-i përfshin 61 bashkitë e Shqipërisë sipas INSTAT-it dhe çdo qytet shtesë nga pikat e regjistruara. Qytetet pa pika shfaqin listë bosh. Rrota e mausit zmadhon hartën rreth kursorit; butoni **Vendndodhja ime** rikthen pozicionin e drejtuesit. Për pikën e zgjedhur, Driver-i hap udhëzimet e makinës me koordinatat e saj. Google Maps është parazgjedhja; Apple Maps mund të zgjidhet te **Cilësimet**, dhe preferenca ruhet në këtë shfletues. Operatori sheh pikat dhe përmbledhjen e rrjetit; Superadmin mund t’i shtojë, ndryshojë dhe arkivojë ato. Janë implementuar hyrja, profili personal, gjuha sq/en, ndryshimi i fjalëkalimit, dalja dhe lista e njoftimeve. Emri/roli, gjuha dhe dalja janë në menunë e llogarisë në krye.

Te formulari i pikës, Superadmin hap hartën OpenStreetMap, lëviz/zmadho pamjen dhe klikon vendin e saktë. Pas konfirmimit, latituda dhe longituda plotësohen në formular; adresa shkruhet veçmas dhe pika ruhet me butonin **Ruaj pikën**. Caktimet e pikave te Admin, sesionet reale dhe statistikat nga telemetria janë hapat e ardhshëm. Gjendja e pikave në panel është manuale deri te integrimi OCPP. Të dhënat e Driver-it në host-in lokal janë demo.

## Kontrolli

Përshtatjet për telefonin janë në `src/app/mobile.css`. Admin dhe Superadmin kanë një rresht navigimi me pesë butona dhe panelin **Më shumë** për seksionet e tjera. [Kontrolli i kodit, faqet e shqyrtuara dhe kufijtë e verifikimit](../docs/mobile-layout-audit.md).

Regjistrimi i drejtuesit është te `/register`, i lidhur nga faqja e hyrjes. Emri, mbiemri, email-i dhe celulari kërkohen bashkë me fjalëkalimin. Numri përfshin kodin e shtetit, p.sh. `+355691234567`; ruhet në profil. Pas regjistrimit hapet `/driver`.

```powershell
npm run test
npm run test:coverage
npm run typecheck
npm run build
```

Paketa të instaluara lokalisht dhe `package-lock.json` i krijuar nga cache. `scripts/next-local.cjs` çaktivizon telemetrinë dhe riparimin automatik online të lockfile-it. Baseline Next 15.2.8 është për punën lokale; versionet/advisories duhen kontrolluar përpara publikimit.

## Nisja me API reale

Vendos variablat e shembullit `.env.example` në mjedisin e procesit ose në `.env.local` të injoruar nga Git:

```powershell
$env:EVCHARGING_API_BASE_URL = 'http://127.0.0.1:5201'
$env:EVCHARGING_WEB_ORIGIN = 'http://127.0.0.1:3100'
npm run dev
```

Hap `http://127.0.0.1:3100`. Përdor Superadmin-in e krijuar për databazën EVCharging. Nuk ka fjalëkalim parazgjedhje në API reale.

Për host-in e përkohshëm të testit, përdor API `http://127.0.0.1:5202` dhe llogarinë Driver `driver@example.test` / `Local-driver-demo-2026!`. Pas hyrjes hapet `/driver`, me 10 pika demo në hartën OpenStreetMap dhe 6 seanca te `/driver/history`. Pika demo **Qendra e Lushnjës** shfaq vetëm kohën e mbetur për lidhësin 1, me vlerësim ilustrues prej rreth 40 minutash në nisje të host-it. Drejtuesi nuk fut kohë udhëtimi. Vlerësimi nuk është rezervim dhe nuk vjen nga pajisje reale. Të dhënat zhduken me ndalimin e host-it; [detajet e host-it](../backend/README.md).

Pllakat e hartës merren nga `https://tile.openstreetmap.org` vetëm kur hapet pamja e hartës. Kërkohet internet në shfletues. Harta ka atribim të dukshëm; referenca është [politika e OpenStreetMap](https://operations.osmfoundation.org/policies/tiles/).

Cookie është HttpOnly/SameSite strict dhe Secure në production. Origin duhet të përputhet saktësisht për POST. Token-i nuk ruhet në localStorage. Rrugët BFF kanë allowlist dhe body limit. Production kërkon URL API me HTTPS.

## Verifikimi i kryer

Më 4 tetor kaluan 114 teste, typecheck dhe build-i production. Testet përfshijnë handler-at BFF dhe formularin e regjistrimit; nuk provojnë rrjedhën në shfletues me API/DB reale. Mbulimi production është 89.28% rreshta/78.67% degë vetëm në 28 nga 62 skedarë. `test:coverage` liston edhe skedarët jashtë matjes. [Rezultatet dhe kufijtë](../docs/release-readiness-audit.md).
