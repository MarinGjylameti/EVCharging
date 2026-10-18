import * as Location from "expo-location";
import { validCoordinates, type Coordinates } from "../../shared/map-geometry";
import type { MapSession } from "./map-camera";

async function locate(): Promise<Coordinates | null> {
  try {
    let permission = await Location.getForegroundPermissionsAsync();
    if (!permission.granted && permission.canAskAgain) permission = await Location.requestForegroundPermissionsAsync();
    if (!permission.granted || !await Location.hasServicesEnabledAsync()) return null;
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      const result = await Promise.race([
        Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced, mayShowUserSettingsDialog: false }),
        new Promise<null>(resolve => { timer = setTimeout(() => resolve(null), 12000); }),
      ]);
      return result && validCoordinates(result.coords) ? { latitude: result.coords.latitude, longitude: result.coords.longitude } : null;
    } finally { clearTimeout(timer); }
  } catch { return null; }
}

export function readInitialLocation(session: MapSession) {
  // One foreground fix per app session; no watcher, persistence or API upload.
  return session.locationRequest ??= locate().then(location => { session.location = location; return location; });
}
