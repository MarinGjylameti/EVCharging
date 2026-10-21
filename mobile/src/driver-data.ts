import type { DriverChargingSession, DriverChargingState, Notification, Station, User } from "../../contracts/account";
import { request } from "./api";

export type DriverSection = "notifications" | "stations" | "sessions";
export type SectionState = { loaded: boolean; pending: boolean; error: string };
export type DriverData = { user: User; notifications: Notification[]; stations: Station[]; sessions: DriverChargingSession[]; sections: Record<DriverSection, SectionState> };
export function emptyDriverData(): Omit<DriverData, "user"> {
  return { notifications: [], stations: [], sessions: [], sections: {
    notifications: { loaded: false, pending: false, error: "" },
    stations: { loaded: false, pending: false, error: "" },
    sessions: { loaded: false, pending: false, error: "" },
  } };
}
// Coalesce foreground/pull/push refreshes and discard responses from a previous login.
export function createDriverLoader(deliver: (data: DriverData) => void) {
  let token: string | null = null;
  let revision = 0;
  let pending: Promise<void> | null = null;
  let controller: AbortController | null = null;
  let data = emptyDriverData();
  const endedSessions = new Set<string>();
  return {
    setSession(value: string | null) { controller?.abort(); token = value; revision++; pending = null; data = emptyDriverData(); endedSessions.clear(); },
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
        const active = () => epoch === revision && !attempt.signal.aborted;
        const publish = () => {
          if (active()) deliver({ ...data, user, sessions: data.sessions.filter(session => !endedSessions.has(session.id)) });
        };
        const reads = {
          notifications: () => request<Notification[]>("/me/notifications", current, "GET", undefined, attempt.signal),
          stations: () => request<Station[]>("/driver/stations", current, "GET", undefined, attempt.signal),
          sessions: async () => (await request<DriverChargingState>("/driver/charging-sessions", current, "GET", undefined, attempt.signal)).sessions,
        };
        const sections = Object.keys(reads) as DriverSection[];
        data = { ...data, sections: { ...data.sections } };
        for (const section of sections) data.sections[section] = { ...data.sections[section], pending: true };
        publish();
        // Publish each completed section immediately. Optional reads cannot hold
        // up stations/charging, and a failed refresh retains only this user's cache.
        await Promise.all(sections.map(async section => {
          try {
            const value = await reads[section]();
            if (!active()) return;
            data = { ...data, [section]: value, sections: { ...data.sections, [section]: { loaded: true, pending: false, error: "" } } };
            publish();
          } catch (failure) {
            if (!active()) return;
            if ((failure as { status?: number })?.status === 401 || failure instanceof Error && failure.message === "session_expired") {
              attempt.abort();
              throw failure;
            }
            data = { ...data, sections: { ...data.sections, [section]: { ...data.sections[section], pending: false, error: failure instanceof Error ? failure.message : "genericError" } } };
            publish();
          }
        }));
      };
      const promise = run().catch(error => {
        if (epoch !== revision) return;
        if (!attempt.signal.aborted) {
          // A failed identity read makes every retained section stale. Keep that
          // state until each section succeeds after authentication recovers.
          data = { ...data, sections: { ...data.sections } };
          for (const section of Object.keys(data.sections) as DriverSection[]) {
            data.sections[section] = { ...data.sections[section], pending: false, error: error instanceof Error ? error.message : "genericError" };
          }
          throw error;
        }
        if ((error as { status?: number })?.status === 401 || error instanceof Error && error.message === "session_expired") throw error;
      }).finally(() => {
        signal?.removeEventListener("abort", abort);
        if (epoch === revision) { pending = null; controller = null; }
      });
      pending = promise;
      return promise;
    },
  };
}
