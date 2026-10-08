import type { ChargingHistory, Notification, Station, User } from "../../contracts/account";
import { request } from "./api";

export type DriverData = { user: User; notifications: Notification[]; stations: Station[]; history: ChargingHistory[] };
// Coalesce foreground/pull/push refreshes and discard responses from a previous login.
export function createDriverLoader(deliver: (data: DriverData) => void) {
  let token: string | null = null;
  let revision = 0;
  let pending: Promise<void> | null = null;
  let controller: AbortController | null = null;
  return {
    setSession(value: string | null) { controller?.abort(); token = value; revision++; pending = null; },
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
        const [notifications, stations, history] = await Promise.all([
          request<Notification[]>("/me/notifications", current, "GET", undefined, attempt.signal), request<Station[]>("/driver/stations", current, "GET", undefined, attempt.signal), request<ChargingHistory[]>("/driver/history", current, "GET", undefined, attempt.signal),
        ]);
        if (epoch === revision && !attempt.signal.aborted) deliver({ user, notifications, stations, history });
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
