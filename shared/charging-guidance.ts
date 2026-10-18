import type { Language, StationCharging } from "../contracts/account";

// An expired waiting request is released by the server on the next connect.
// Linked claims do not expire: the charger must confirm the end of its session.
export function blockingConnection(view: StationCharging, now: number) {
  const blocker = view.canConnect ? view.blockingConnection : null;
  return blocker && (blocker.state === "Linked" || Date.parse(blocker.expiresAt) > now) ? blocker : null;
}

export function blockingConnectionGuidance(state: "Waiting" | "Linked", language: Language) {
  const sq = language === "sq";
  return state === "Waiting" ? {
    title: sq ? "Ke një pritje në një pikë tjetër" : "You are waiting at another charger",
    body: sq ? "Hap pikën e mëparshme. Mund të vazhdosh aty ose të anulosh pritjen përpara se të zgjedhësh këtë pikë." : "Open the previous charger. Continue there or cancel that waiting request before choosing this one.",
    action: sq ? "Shko te pritja" : "Open waiting request",
  } : {
    title: sq ? "Ke një seancë ende të hapur" : "You still have an open session",
    body: sq ? "Hap seancën për të parë gjendjen. Nëse e ke ndalur karikimin, presim konfirmimin nga karikuesi. Nëse nuk mbërrin, kontakto operatorin e pikës." : "Open the session to check its status. If you have stopped charging, we are waiting for the charger's confirmation. If it does not arrive, contact the station operator.",
    action: sq ? "Shko te seanca" : "Open existing session",
  };
}

// Guidance describes server-confirmed state; opening a station never claims a charge.
export function chargingGuidance(view: StationCharging, language: Language, stale: boolean) {
  const sq = language === "sq";
  const message = (title: string, body: string, steps: string[] = []) => ({ title, body, steps });
  if (stale) return message(sq ? "Po kontrollojmë lidhjen" : "Checking the connection",
    sq ? "Gjendja e fundit mund të ketë ndryshuar. Prit përditësimin përpara se të veprosh te karikuesi." : "The last status may have changed. Wait for an update before using the charger.");
  if (view.station.status === "Maintenance" || view.station.status === "OutOfService"
    || view.connector?.status === "Faulted" || view.connector?.status === "Unavailable"
    || view.connector?.deviceStatus === "Faulted" || view.connector?.deviceStatus === "Unavailable")
    return message(sq ? "Ky lidhës nuk është në shërbim" : "This connector is unavailable",
      sq ? "Zgjidh një lidhës tjetër ose kontakto operatorin e pikës." : "Choose another connector or contact the station operator.");
  if (view.inUse) return message(sq ? "Lidhësi është në përdorim" : "Connector in use",
    sq ? "Zgjidh një lidhës tjetër ose prit që të lirohet. Karikimi i përdoruesit tjetër është privat." : "Choose another connector or wait until it is available. The other driver's charge is private.");
  if (view.connector?.status === "Reserved" || view.connector?.status === "Finishing")
    return message(sq ? "Lidhësi ende nuk është i lirë" : "Connector is not available yet",
      sq ? "Prit gjendjen E lirë ose zgjidh një lidhës tjetër." : "Wait for Available status or choose another connector.");
  if (!view.deviceConnected) return message(sq ? "Karikuesi nuk është i lidhur" : "Charger is offline",
    sq ? "Presim rilidhjen e pajisjes. Nëse vonon, zgjidh një pikë tjetër ose kontakto operatorin." : "Waiting for the charger to reconnect. If it takes too long, choose another station or contact the operator.");
  if (view.station.status === "Unknown" || view.connector?.status === "Unknown")
    return message(sq ? "Në pritje të gjendjes së lidhësit" : "Waiting for connector status",
      sq ? "Pajisja është lidhur, por ende nuk ka konfirmuar gjendjen. Prit përditësimin." : "The charger is connected but has not confirmed its status yet. Wait for an update.");
  if (view.driverAccessMode !== "Card") return message(sq ? "Përdor kartën tënde të karikimit" : "Use your charging card",
    sq ? "Për t'i parë matjet këtu, operatori duhet ta ketë lidhur kartën me llogarinë tënde. Hapja e kësaj pike nuk të cakton një karikim." : "To see readings here, the operator must link your card to your account. Opening this station does not assign a charge to you.");
  return message(sq ? "Si të nisësh karikimin" : "How to start charging",
    sq ? "Ndiq udhëzimet në karikues. Seanca jote shfaqet automatikisht pasi pajisja konfirmon nisjen." : "Follow the instructions on the charger. Your session appears automatically once the charger confirms the start.",
    sq ? ["Kontrollo numrin e lidhësit dhe lidh makinën", "Identifikohu me kartën e lidhur me llogarinë", "Shiko matjet e karikimit këtu"]
      : ["Check the connector number and plug in", "Identify yourself with your linked card", "Follow your charging readings here"]);
}
