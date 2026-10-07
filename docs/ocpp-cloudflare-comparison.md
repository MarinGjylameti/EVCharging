# Provë krahasimi me Cloudflare

Kjo provë përdor të njëjtin endpoint WebSocket dhe përpunues OCPP si API-ja jonë,
me një host lokal dhe hyrje publike Cloudflare, si konfigurimi që punon te kolegu.
Host-i përdor databazë të përkohshme në memorie: të dhënat humbasin kur ndalet dhe
nuk shfaqen në web-in e Render-it. Nuk provon PostgreSQL ose rrjetin e Render-it.
Nuk krijon përdorues apo pika demonstrimi.

Nga rrënja e EVCharging, pas build-it lokal:

```powershell
dotnet backend/tools/EVCharging.OcppDiagnosticHost/bin/Debug/net10.0/EVCharging.OcppDiagnosticHost.dll --local-ocpp-test
```

Kur shfaqet `Now listening on: http://127.0.0.1:9000`, në një terminal tjetër:

```powershell
cloudflared tunnel --url http://127.0.0.1:9000
```

Komanda e tunelit hyn në internet. Në këtë projekt kërkon autorizim përpara
ekzekutimit; nuk niset automatikisht nga host-i. `cloudflared` duhet të jetë
i disponueshëm; nuk instalohet globalisht nga ky mjet.

Cloudflare afishon një adresë të re HTTPS. Në fushën bazë të karikuesit vendos:

```text
wss://<adresa-e-re>.trycloudflare.com/ws/cs
```

Pajisja duhet të shtojë ID-në vetëm një herë, duke formuar
`/ws/cs/NKR2023072805`. Mbaj të dy terminalet hapur gjatë provës. Loget e host-it
duhet të tregojnë `OCPP connection attempt`, `connected` dhe mesazhet e përpunuara.
Mbyllja bëhet me Ctrl+C në secilin terminal.

Nëse pajisja dërgon Boot/Status/Heartbeat këtu, kodi ynë funksionon për këtë
pajisje me hyrjen Cloudflare. Mbeten për t'u dalluar TLS/rrjeti i Render-it,
konfigurimi i deploy-it dhe databaza. Dështimi këtu ende kërkon loget e tentativës;
nuk provon vetë defekt të karikuesit.
