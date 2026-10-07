# Dashboard calendar analytics

The dashboard uses one authenticated `GET /api/v1/admin/analytics` response for all historical panels. The web BFF forwards the selected calendar dates; PostgreSQL filters and aggregates authorized sessions. No raw session collection is downloaded to calculate the dashboard.

The network calendar defaults to `Europe/Tirane`. Dates are `YYYY-MM-DD`. Bounds are independently converted to UTC, including daylight-saving changes. The inclusive **Until date** becomes an exclusive boundary at local midnight the following day. Data is clipped at the request time; future chart buckets remain `null`, rather than implying zero activity.

| Period | Selection | Trend | Previous comparison |
| --- | --- | --- | --- |
| Daily | One day; today by default | Hourly, including 23/25-hour DST days | Previous calendar day |
| Weekly | Monday through Sunday; current week by default | Daily | Previous Monday–Sunday week |
| Monthly | One calendar month; current month by default | Daily, including every day in the month | Previous calendar month |
| Custom | Required From/Until calendar dates | Up to 2 days: hourly; up to 62 days: daily; up to 434 days: weekly; otherwise monthly | Immediately preceding range with the same number of calendar days |

Custom weekly/monthly buckets align with the calendar; the first and last buckets may be partial. Very long ranges combine multiple months into each bucket to keep the chart at about 62 points. The response identifies granularity and bucket size. Selecting a period starting in the future is rejected; current weeks/months may include future dates, but no future data is returned. Custom Until cannot exceed today.

Session counts use **start dates**. Energy and calculated cost use **completion dates**, including sessions that started before the selected range. They represent final session totals assigned to the completion bucket, not meter energy apportioned across hours or days. Costs use the saved session tariff and round each session to two decimal places. Completed sessions with missing energy are counted and flagged, but excluded from energy/cost totals. Demo sessions are included, as in the existing operational summary.

Station/location rankings show the top 10 by completed-session energy, then starts, within the same period and authorization scope. Outcomes describe sessions completed in the period: completed, interrupted by an exceptional stop reason, or needing verification. Peak usage identifies the bucket with the most starts; its interval uses the selected chart granularity. It is not an estimate of concurrent charger utilization.

Comparisons use the full immediately preceding equivalent period. A current period is marked **so far**, because it may be incomplete. An empty previous period returns a null percentage when current activity is nonzero, instead of an infinite percentage. Both empty periods return zero.

Active sessions, charger states, active locations and global account counts are returned separately under `current` and remain current when historical periods are selected. Archived stations remain visible in authorized historical analytics but are excluded from current station counts. Admin grants apply to every query; only Superadmin sees global account counts. A PostgreSQL repeatable-read transaction keeps the historical panels consistent within one response while sessions change.

Examples:

```text
/api/v1/admin/analytics?period=Daily&date=2026-10-07&timeZone=Europe%2FTirane
/api/v1/admin/analytics?period=Weekly&date=2026-10-07&timeZone=Europe%2FTirane
/api/v1/admin/analytics?period=Monthly&date=2026-10-01&timeZone=Europe%2FTirane
/api/v1/admin/analytics?period=Custom&from=2026-09-01&until=2026-10-07&timeZone=Europe%2FTirane
```

The existing `/admin/summary` contract is retained for compatibility; the dashboard uses `/admin/analytics`. This feature needs both API and web deployments and no database migration.

Local tests cover calendar boundaries, DST, SQL translation of bucket grouping, authorization, aggregation consistency, navigation, range selection and cancellation of outdated requests. The PostgreSQL integration case runs in the existing Verification workflow against its disposable database; it is skipped locally without the explicit PostgreSQL opt-in.
