import type { ChargingHistory, DriverChargingSession, DriverChargingState, Notification, Station, User } from "../../contracts/account";
import { request } from "./api";

export type DriverData = { user: User; notifications: Notification[]; stations: Station[]; history: ChargingHistory[]; sessions: DriverChargingSession[] };
// Coalesce foreground/pull/push refreshes and discard responses from a previous login.
export function createDriverLoader(deliver: (data: DriverData) => void) {
  let token: string | null = null;
  let revision = 0;
  let pending: Promise<void> | null = null;
  let controller: AbortController | null = null;
  const endedSessions = new Set<string>();
  return {
    setSession(value: string | null) { controller?.abort(); token = value; revision++; pending = null; endedSessions.clear(); },
    // A slower list response must not revive a session already confirmed ended.
    confirmSessionEnded(id: string) { endedSessions.add(id); },
    refresh(signal?: AbortSignal): Promise<void> {
      if (!token) return Promise.resolve();
      if (pending) return pending;
      const current = token, epoch = revision;
      const attempt = new AbortController(); controller = attempt;
      const abort = () => attempt.abort();
      if (signal?.aborted) abort();
      signal?.addEventListener("abort", abort, { once: true });
      const run = async () => {
        const user = await request<User>("/me", current, "GET", undefined, attempt.signal);
        if (epoch !== revision || attempt.signal.aborted) return;
        if (user.role !== "DRIVER") throw new Error("administratorAccount");
        const [notifications, stations, history, charging] = await Promise.all([
          request<Notification[]>("/me/notifications", current, "GET", undefined, attempt.signal), request<Station[]>("/driver/stations", current, "GET", undefined, attempt.signal), request<ChargingHistory[]>("/driver/history", current, "GET", undefined, attempt.signal),
          request<DriverChargingState>("/driver/charging-sessions", current, "GET", undefined, attempt.signal),
        ]);
        if (epoch === revision && !attempt.signal.aborted) deliver({ user, notifications, stations, history, sessions: charging.sessions.filter(session => !endedSessions.has(session.id)) });
      };
      const promise = run().catch(error => { if (epoch === revision && !attempt.signal.aborted) throw error; }).finally(() => {
        signal?.removeEventListener("abort", abort);
        if (epoch === revision) { pending = null; controller = null; }
      });
      pending = promise;
      return promise;
    },
  };
}
