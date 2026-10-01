# Dependencies for the first implementation

Project-local packages only. The initial .NET/Next/Expo baselines come from the local HOUSS manifests and installed cache. They are not a claim of current security support. Web and NuGet lockfiles are generated from the offline baseline. Before release, check supported patch versions and security advisories and refresh/verify lockfiles for the deployment platform. NuGet auditing is disabled only for explicitly offline restores.

- Backend: .NET 10, EF Core, Npgsql, xUnit, ASP.NET Core test host. .NET/ASP.NET shared framework supplies Identity password hashing, authentication primitives, data protection and rate limiting. The implemented API uses a custom opaque bearer scheme; Next handles the browser cookie.
- Web: Next.js, React, TypeScript and type declarations.
- Mobile: Expo, React Native, SecureStore, Notifications, Constants, Device and Safe Area Context.
- Installs and CLI caches must stay under EVCharging. No global installs or changes to HOUSS.
- Internet downloads and documentation verification await the user's network authorization. Localhost testing was explicitly approved. Offline restores can use the existing local cache.

This milestone uses revocable opaque bearer sessions for mobile, stored as hashes in PostgreSQL, and a protected HttpOnly session cookie for web. No home-built JWT signing scheme. Refresh-token rotation is not implemented in this initial slice; sessions have an absolute configurable expiry and the client signs in again after expiry. This deliberate simplification is recorded in the implementation status and can evolve behind the auth contract.
