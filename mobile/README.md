# Mobile — iOS dhe Android

Një projekt Expo/React Native për të dy platformat. Kodi lidh regjistrimin dhe hyrjen e Driver-it, profilin, gjuhën sq/en, njoftimet, listën e pikave dhe historikun personal me API-në EVCharging. Pikat filtrohen sipas qytetit/disponueshmërisë; butoni **Hap në hartë** përdor aplikacionin e hartës së telefonit. Përmbajtja demo shënohet qartë.

## Gjendja e verifikimit

U kontrollua sintaksa e 10 skedarëve TypeScript/TSX me TypeScript-in lokal. `npm install` për Expo u provua me lejen e përdoruesit, por mjedisi ktheu `EACCES` për `registry.npmjs.org`. Kështu mungojnë ende `node_modules`, typecheck i plotë, build-et native dhe prova në pajisje. Nuk ka APK/IPA të gatshme.

## Nisja pasi paketat të jenë të disponueshme

1. Instalo paketat lokalisht në këtë dosje: `npm install`.
2. Vendos `EXPO_PUBLIC_API_BASE_URL` sipas `.env.example`; adresa duhet të arrihet nga pajisja. Host-i i përkohshëm `BrowserTestHost` dëgjon vetëm në loopback dhe nuk arrihet nga një telefon fizik.
3. Ekzekuto `npm run typecheck`, pastaj `npm run start` dhe hap projektin në iOS ose Android.

Për provë me karikues realë duhen ende identifikimi i pajisjes/OCPP, lidhja e sigurt e QR me Driver-in dhe sesionin, si edhe testimi i push në pajisje. Aplikacioni nuk kryen pagesa ose nis karikim real.
