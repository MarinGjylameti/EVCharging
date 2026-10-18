import { toWorldPixels, visibleTiles, type Coordinates, type MapView } from "../../shared/map-geometry";

export function mapTranslation(view: MapView, origin: Coordinates) {
  const anchor = toWorldPixels(origin, view.zoom), center = toWorldPixels(view.center, view.zoom);
  return { x: anchor.x - center.x, y: anchor.y - center.y };
}

// A stable local coordinate space lets a native transform move the map and its
// markers together. React only needs new tiles when the visible tile set changes.
export function mapScene(view: MapView, origin: Coordinates, width: number, height: number) {
  const zoom = Math.floor(view.zoom), scale = 2 ** (view.zoom - zoom), size = 256 * scale;
  const offset = mapTranslation(view, origin);
  const tiles = visibleTiles(view.center, zoom, width / scale, height / scale).map(tile => ({
    ...tile, left: tile.left * scale - offset.x, top: tile.top * scale - offset.y,
  }));
  const left = tiles.length ? Math.min(...tiles.map(tile => tile.left)) : 0;
  const top = tiles.length ? Math.min(...tiles.map(tile => tile.top)) : 0;
  const right = tiles.length ? Math.max(...tiles.map(tile => tile.left)) + size : 0;
  const bottom = tiles.length ? Math.max(...tiles.map(tile => tile.top)) + size : 0;
  return { zoom, size, tiles, left, top, right, bottom, key: `${zoom}:${tiles.map(tile => `${tile.x},${tile.y}`).join(";")}` };
}
