# Driver-i demo dhe caktimi sipas kartës

## Konfigurimi në panel

Superadmin hap **Pikat → Karikuesi fizik → Kush i sheh të dhënat e karikuesit?**
Pajisja duhet të jetë regjistruar më parë. Zgjidhet një llogari Driver aktive
dhe plotësohet, ose lihet bosh, **Kodi i kartës / JSON**.

| Konfigurimi | Sjellja |
|---|---|
| Driver i zgjedhur, fusha bosh | Demonstrim: Driver-i merr sinjalet e pajisjes edhe pa sesion dhe çdo karikim të ri të pajisjes. Nuk filtrohet idTag. |
| Driver i zgjedhur, fusha e plotësuar | Vetëm sesionet që nisin me atë idTag i caktohen këtij Driver-i. Nuk ka fallback demo për një kartë tjetër. |
| Pa Driver | Nuk ka caktim automatik. Lista ekzistuese e kartave të lejuara vazhdon të kontrollojë nisjen; sesionet pa pronar shihen në panelin operativ. |

Kjo vlen për pajisjen e zgjedhur, jo për çdo karikues të platformës. Ky version
mban një caktim Driver/kartë për pajisje; nuk është ende regjistër i shumë
kartave të Driver-ëve për të njëjtën pajisje.

Fusha pranon kod të thjeshtë, p.sh. `CARD-001`, ose:

```json
{"idTag":"CARD-001"}
```

Pranon edhe mesazhin OCPP të plotë:

```json
[2,"request-1","Authorize",{"idTag":"CARD-001"}]
```

Për `StartTransaction` pranohet e njëjta strukturë me payload-in e nisjes.
Ruhet vetëm idTag me 1–20 karaktere, me shkronjat/hapësirat që ka brenda JSON-it.
JSON i gabuar ose idTag bosh/numerik refuzohet; nuk kalohet në demo në heshtje.
Kodi i kartës së caktuar lejohet të karikojë. Karta të tjera nga lista e
lejuar e pajisjes mund të nisin sesione, por këto mbeten pa pronar Driver;
ato nuk i shfaqen përdoruesit me kartë tjetër.

## Hapja e aplikacionit me vonesë

Në nisjen fizike krijohet caktimi persistent me Driver-in. Ai nuk duhet të
shtypë “Lidhu” ose të jetë online. Kur e hap faqen 10–30 minuta më vonë,
snapshot-i lexon sesionin dhe matjet e ruajtura nga nisja; më pas vijnë
përditësimet live. Ndalimi ruan historikun te i njëjti Driver. Matjet dhe
koha e nisjes nuk rivendosen kur hapet faqja.

Driver-i mund të ketë disa sesione fizike të caktuara automatikisht për demo.
Në faqen e pajisjes zgjedh **Lidhësi 1**, **Lidhësi 2**, etj. Mesazhet e një
lidhësi nuk mbishkruajnë pamjen e tjetrit. Kufizimi i një pritjeje/lidhjeje
manuale për Driver ruhet për simulatorin. Profili i automjetit merret si
snapshot në nisjen automatike; ndryshimi i tij nuk rishkruan disa makina demo.

Caktimi ndryshohet vetëm kur nuk ka sesion aktiv në atë pikë. Nuk i
transferon historikun përdoruesit të ri dhe nuk merr në pronësi sesione
të vjetra pa pronar. Diagnostikimi pa sesion për demo fillon nga koha e
caktimit. Kartat e lidhura marrin diagnostikimin e sesionit të tyre.

Hapja e faqes nuk nis karikim fizik. Nëse pajisja kërkon skanimin e kartës
për të nisur, ky kusht mbetet te pajisja. Boot/Heartbeat/Status dhe matjet pa
transaksion shfaqen në demo vetëm nëse dërgohen realisht nga karikuesi.

## Deploy dhe migrimi

Migrimi `PhysicalChargerDriverAssignments` shton Driver-in, idTag-n dhe
kohën e caktimit te pajisja, si dhe flamurin e caktimeve automatike. Ai
ruan sesionet/historikun dhe caktimet e vjetra manuale. Unikësia e pronarit
për sesion dhe e sesionit të lidhur për lidhës mbetet në fuqi. Indeksi për
një lidhje aktive për Driver zbatohet për caktimet manuale.

Duhet deploy i backend-it me `EVCHARGING_MIGRATE_ON_START=true` dhe deploy
i web-it. Pastaj Superadmin zgjedh llogarinë demo në panel dhe ruan fushën
bosh. Nuk është vendosur automatikisht një email nga kodi. Prova me
pajisjen reale dhe databazën e deploy-t kërkon verifikim pas publikimit.

## Verifikimi lokal më 5 tetor 2026

- Backend: build me 0 gabime/paralajmërime; 191 raste testimi të kaluara,
  15 teste PostgreSQL të paekzekutuara. VSTest u bllokua nga socket-i lokal
  i Windows (10013), prandaj rastet xUnit u thirrën brenda të njëjtit proces,
  duke përdorur ASP.NET TestServer në memorie. Nuk pretendohet provë e
  locking/FK/rollback në PostgreSQL.
- Web: 129 teste dhe TypeScript kaluan. Mbulohen formulari, përcjellja e
  JSON-it, pamja fizike pa buton Lidhu dhe izolimi i lidhësve.
- Migrimi: modeli/snapshot-i përputhen dhe SQL-ja idempotente u gjenerua
  offline; migrimi nuk u aplikua në një databazë reale.
- Build-i production i web-it u bllokua gjatë ngarkimit të next.config.ts:
  SWC nuk mundi të zgjidhte shtegun lokal në sandbox-in Windows (Access denied).
  Nuk u krye deploy ose provë me pajisje reale.
