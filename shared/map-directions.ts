import type { Coordinates } from "./map-geometry";

export type MapProvider = "google" | "apple";

export const mapProviderStorageKey = "evcharging_map_choice";

export function mapProviderFromPreference(value: unknown): MapProvider {
  return value === "apple" ? "apple" : "google";
}

export function directionsUrl(provider: MapProvider, destination: Coordinates): string {
  if (!Number.isFinite(destination.latitude) || !Number.isFinite(destination.longitude) ||
    Math.abs(destination.latitude) > 90 || Math.abs(destination.longitude) > 180)
    throw new RangeError("Invalid destination coordinates.");
  const point = `${destination.latitude.toFixed(6)},${destination.longitude.toFixed(6)}`;
  if (provider === "apple") {
    const url = new URL("https://maps.apple.com/");
    url.searchParams.set("daddr", point);
    url.searchParams.set("dirflg", "d");
    return url.toString();
  }
  const url = new URL("https://www.google.com/maps/dir/");
  url.searchParams.set("api", "1");
  url.searchParams.set("destination", point);
  url.searchParams.set("travelmode", "driving");
  url.searchParams.set("dir_action", "navigate");
  return url.toString();
}
