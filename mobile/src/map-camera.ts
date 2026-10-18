import { distanceKm, fitMapView, toWorldPixels, validCoordinates, type Coordinates, type MapView } from "../../shared/map-geometry";

// Kept in App memory only: returning from charging/list must not reset the map.
export type MapSession = {
  view?: MapView;
  interacted?: boolean;
  focused?: boolean;
  location?: Coordinates | null;
  locationRequest?: Promise<Coordinates | null>;
};

function fits(point: Coordinates, view: MapView, width: number, height: number) {
  const position = toWorldPixels(point, view.zoom), center = toWorldPixels(view.center, view.zoom);
  return Math.abs(position.x - center.x) <= Math.max(1, width / 2 - 55)
    && Math.abs(position.y - center.y) <= Math.max(1, height / 2 - 60);
}

export function fitMobileMap(points: Coordinates[], width: number, height: number): MapView {
  const valid = points.filter(validCoordinates), view = fitMapView(valid, width, height);
  // A driver can be abroad. The former minimum zoom of 6 could hide one endpoint.
  while (view.zoom > 0 && valid.some(point => !fits(point, view, width, height))) view.zoom--;
  return view;
}

export function initialMapView(stations: Coordinates[], location: Coordinates | null | undefined, width: number, height: number): MapView {
  if (!location || !validCoordinates(location)) return fitMobileMap(stations, width, height);
  const nearby = stations.filter(validCoordinates).reduce<Coordinates | null>((nearest, station) =>
    !nearest || distanceKm(location, station) < distanceKm(location, nearest) ? station : nearest, null);
  const local = { center: location, zoom: 15 };
  return !nearby || fits(nearby, local, width, height) ? local : fitMobileMap([location, nearby], width, height);
}
