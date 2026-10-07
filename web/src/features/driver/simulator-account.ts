import type { Language, StationCharging } from "../../../../contracts/account";

export class SimulatorAccountError extends Error {
  readonly code: string;
  readonly traceId: string | null;
  constructor(readonly status: number, code: unknown, traceId: unknown = null) {
    const safeCode = typeof code === "string" && /^[a-z][a-z0-9_]{0,63}$/.test(code) ? code : "association_failed";
    super(safeCode); this.code = safeCode;
    this.traceId = typeof traceId === "string" && /^[A-Za-z0-9:_-]{1,128}$/.test(traceId) ? traceId : null;
  }
}

export function simulatorAccountFailureMessage(failure: unknown, language: Language) {
  const sq = language === "sq";
  if (!(failure instanceof SimulatorAccountError)) return sq
    ? "Lidhja e llogarisë nuk u konfirmua. Kontrollo lidhjen me shërbimin dhe provo përsëri."
    : "Account association was not confirmed. Check the service connection and retry.";
  let message: string;
  if (failure.status === 401) message = sq
    ? "Seanca e hyrjes ka skaduar. Hyr përsëri në llogari."
    : "Your login session has expired. Sign in again.";
  else if (failure.code === "driver_already_connected") message = sq
    ? "Llogaria jote është e lidhur me një pikë tjetër. Përfundo karikimin ose anulo kërkesën në pritje atje."
    : "Your account is associated with another charger. Finish that charge or cancel its waiting request.";
  else if (failure.code === "charger_in_use") message = sq
    ? "Pika ka një lidhje ose kërkesë të një Driver-i tjetër, ose një transaksion aktiv. Pronësia nuk u ndryshua."
    : "The charger has another Driver's association or waiting request, or an active transaction. Ownership was not changed.";
  else if (failure.code === "test_connection_unavailable") message = sq
    ? "Lidhja demo nuk është e aktivizuar për këtë pikë. Kontrollo konfigurimin e simulatorit në mjedisin e testit."
    : "Demo association is not enabled for this charger. Check simulator configuration in the test environment.";
  else if (failure.status === 403) message = sq
    ? "Kërkesa e lidhjes nuk u lejua. Kontrollo lejet e llogarisë dhe konfigurimin e faqes."
    : "The association request was forbidden. Check account permissions and web configuration.";
  else if (failure.status >= 500) message = sq
    ? "Shërbimi nuk e konfirmoi lidhjen e llogarisë. Provo përsëri kur të jetë i disponueshëm."
    : "The service did not confirm account association. Retry when it is available.";
  else message = sq ? "Kërkesa e lidhjes së llogarisë u refuzua." : "The account association request was rejected.";
  return `${message} (HTTP ${failure.status} · ${failure.code})`;
}

// An unchecked account option explicitly cancels this caller's waiting request.
// It never changes the owner of an existing charge or another driver's waiting request.
export async function prepareSimulatorAccount(stationId: string, associate: boolean, request: typeof fetch = fetch) {
  const response = await request(`/api/driver/stations/${encodeURIComponent(stationId)}/charging`, {
    method: associate ? "POST" : "DELETE",
  });
  const data = await response.json().catch(() => null);
  if (!response.ok) throw new SimulatorAccountError(response.status, data?.code, data?.traceId);
  if (!data || data.station?.id !== stationId) throw new Error("Invalid charging association response.");
  return data as StationCharging;
}
