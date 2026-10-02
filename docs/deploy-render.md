# Demo në Render

Ky konfigurim publikon web-in Next.js, API-n .NET dhe një PostgreSQL të dedikuar për `EVCharging`. Është provë për klientin: stacionet, gjendjet dhe historiku i krijuar nga seed janë ilustrues. OCPP me pajisje reale ende nuk është i lidhur.

## 1. Përgatit repo-n

Pasi të verifikohen ndryshimet, dërgoji në repo-n private `MarinGjylameti/EVCharging`. Render duhet të ketë qasje në këtë repo përmes integrimit të GitHub-it. Mos vendos fjalëkalime, URL të databazës ose token-e në Git.

## 2. Projekti dhe databaza

Në Render krijo projektin `EVCharging` me environment `Demo`. Krijo një **Postgres** në të:

| Fusha | Vlera |
| --- | --- |
| Name | `evcharging-demo-db` |
| Project / Environment | `EVCharging` / `Demo` |
| Database | `evcharging_demo` |
| User | Lëre të gjenerohet nga Render |
| Region | Frankfurt |
| Plan | **Free**, vetëm nëse në fund shfaqet `$0/month` |

Mos kliko krijimin nëse shfaqet çmim mujor. Render lejon vetëm një PostgreSQL falas për workspace. Databaza falas skadon pas 30 ditësh dhe nuk ka backup automatik. Ruaje **Internal Database URL** vetëm në Render; mos e dërgo në chat.

## 3. API-ja

Krijo **New Web Service** nga e njëjta repo private:

| Fusha | Vlera |
| --- | --- |
| Name | `evcharging-api-demo` ose emër i lirë i ngjashëm |
| Project / Environment | `EVCharging` / `Demo` |
| Region | Frankfurt, njësoj si databaza |
| Branch | `main` |
| Root Directory | Lëre bosh; Docker merr vetëm kontekstin `backend` |
| Language / Runtime | Docker |
| Dockerfile Path | `backend/Dockerfile` (nga rrënja e repo-s) |
| Docker Build Context Directory | `backend` |
| Instance Type | Free |
| Health Check Path | `/health/ready` |

Vendos këto **Environment Variables** në shërbimin API:

| Emri | Vlera |
| --- | --- |
| `DATABASE_URL` | **Internal Database URL** nga Postgres |
| `EVCHARGING_MIGRATE_ON_START` | `true` |
| `EVCHARGING_SEED_DEMO_ON_START` | `true` vetëm për këtë demo |
| `EVCHARGING_Demo__DriverEmail` | Email demonstrues që e zgjedh ti |
| `EVCHARGING_Demo__DriverPassword` | Fjalëkalim unik demonstrues që e zgjedh ti |
| `EVCHARGING_Bootstrap__Email` | Email unik për Superadmin-in demonstrues |
| `EVCHARGING_Bootstrap__Password` | Fjalëkalim unik për Superadmin-in demonstrues |
| `AllowedHosts` | `*` për adresën e Render-it |
| `EVCHARGING_Push__Enabled` | `false` |

Fjalëkalimet nuk kanë vlera parazgjedhje në shërbimin real. Mbaji vetëm në Render Environment; mos i shkruaj në repo ose chat. Seed-i shton 10 pika, 6 seanca historike dhe llogaritë demo në mënyrë idempotente. Ai nuk ndryshon fjalëkalimin e një llogarie që ekziston. Pas krijimit, hap `https://<api-url>/health/ready`: pritet HTTP 200. Shiko log-et nëse merr 503. Pas verifikimit të parë, hiq `EVCHARGING_SEED_DEMO_ON_START` dhe fjalëkalimet e seed-it nga mjedisi i shërbimit; llogaritë e krijuara mbeten në databazë.

## 4. Web-i

Krijo një **New Web Service** tjetër nga e njëjta repo:

| Fusha | Vlera |
| --- | --- |
| Name | `evcharging-web-demo` ose emër i lirë i ngjashëm |
| Project / Environment | `EVCharging` / `Demo` |
| Region | Frankfurt |
| Branch | `main` |
| Root Directory | **Bosh**; build-i përdor edhe dosjen `contracts` në rrënjë |
| Language / Runtime | Node |
| Build Command | `cd web && npm ci && npm run build` |
| Start Command | `cd web && ./node_modules/.bin/next start --hostname 0.0.0.0 --port ${PORT:-10000}` |
| Instance Type | Free |

Vendos:

| Emri | Vlera |
| --- | --- |
| `EVCHARGING_API_BASE_URL` | URL publike HTTPS e shërbimit API, pa `/` në fund |
| `EVCHARGING_WEB_ORIGIN` | URL publike HTTPS e vetë web-it, pa `/` në fund |
| `NODE_ENV` | `production` |

Pas nisjes provo hyrjen me llogarinë demonstrative të Driver-it dhe kontrollo hartën e historikun. Web-i aktual i flet API-së nga ana e serverit; klientët e tjerë web që kërkojnë API-n nga shfletuesi kanë nevojë për origjinën e tyre HTTPS te `EVCHARGING_Cors__AllowedOrigins__0`.

## 5. Kufijtë e provës dhe kalimi te klienti

Shërbimet Free flenë pas 15 minutash pa trafik; kërkesa e parë mund të vonojë. Për një CSMS/OCPP që duhet të mbajë lidhje të qëndrueshme me karikuesit, kalimi në plan që qëndron aktiv do të jetë i nevojshëm. Ende nuk ka lidhje OCPP me pajisje reale, ndaj statuset demo nuk janë të dhëna operative.

### Nëse hyrja dështon pas pasivitetit

Free duhet të zgjohet nga një kërkesë HTTP ose lidhje e re WebSocket, pa Manual
Deploy. Hyrja e web-it tani kontrollon `/health/ready` përmes BFF-së me kërkesa
të shkurtra për deri në dy minuta. Faqja HTML e ndezjes së Render nuk trajtohet
si API gati. Vetëm pasi verifikohet API-ja dhe databaza dërgohet një login;
fjalëkalimi nuk dërgohet në çdo retry. Nuk ka polling të vazhdueshëm për ta
mbajtur shërbimin zgjuar.

Nëse pas dy minutash API-ja ende nuk përgjigjet, kontrollo runtime logs për
exception, dalje të procesit ose mungesën e `Now listening`. Gjumi, crash-i,
problemet e databazës dhe kufizimet e workspace-it janë shkaqe të ndryshme;
një mesazh 503 te hyrja nuk i dallon vetë. Mbaj `/health/ready` si Health Check
Path. Pas krijimit fillestar të llogarive, çaktivizo seed-in e nisjes; DB ruan
llogaritë dhe historikun pa e ekzekutuar seed-in në çdo zgjim. Mbaj migrimet
kur publikohet një version që i kërkon.

Render nuk transferon aktualisht shërbime nga një workspace në tjetrin. Kur klienti të marrë përgjegjësinë, krijohen shërbime të reja në workspace-in e tij dhe, nëse ka të dhëna që duhen ruajtur, bëhet eksport/import i databazës. Planifiko këtë përpara përdorimit me të dhëna reale.

Burime: [Render Free](https://render.com/docs/free), [monorepo](https://render.com/docs/monorepo-support), [Postgres](https://render.com/docs/postgresql-creating-connecting), [transferimi i shërbimeve](https://render.com/docs/faq).
