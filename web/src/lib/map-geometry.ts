export type Coordinates = {latitude: number; longitude: number};
export type WorldPoint = {x: number; y: number};

const tileSize = 256;
const maxLatitude = 85.05112878;

export function toWorldPixels(point: Coordinates, zoom: number): WorldPoint {
  const scale = tileSize * 2 ** zoom;
  const latitude = Math.max(-maxLatitude, Math.min(maxLatitude, point.latitude)) * Math.PI / 180;
  const longitude = Math.max(-180, Math.min(180, point.longitude));
  return {
    x: (longitude + 180) / 360 * scale,
    y: (1 - Math.asinh(Math.tan(latitude)) / Math.PI) / 2 * scale
  };
}

export function fromWorldPixels(point: WorldPoint, zoom: number): Coordinates {
  const scale = tileSize * 2 ** zoom;
  const x = Math.max(0, Math.min(scale, point.x));
  const y = Math.max(0, Math.min(scale, point.y));
  return {
    latitude: Math.atan(Math.sinh(Math.PI * (1 - 2 * y / scale))) * 180 / Math.PI,
    longitude: x / scale * 360 - 180
  };
}

export function coordinateAtPixel(center: Coordinates, zoom: number,
  width: number, height: number, x: number, y: number): Coordinates {
  const world = toWorldPixels(center, zoom);
  return fromWorldPixels({x:world.x + x - width / 2, y:world.y + y - height / 2}, zoom);
}

export function zoomAroundPixel(center: Coordinates, fromZoom: number, toZoom: number,
  width: number, height: number, x: number, y: number): Coordinates {
  const anchor = coordinateAtPixel(center, fromZoom, width, height, x, y);
  const world = toWorldPixels(anchor, toZoom);
  return fromWorldPixels({x:world.x - x + width / 2, y:world.y - y + height / 2}, toZoom);
}

export function distanceKm(first: Coordinates, second: Coordinates): number {
  const radians = Math.PI / 180;
  const latitudeDelta = (second.latitude - first.latitude) * radians;
  const longitudeDelta = (second.longitude - first.longitude) * radians;
  const a = Math.sin(latitudeDelta / 2) ** 2 +
    Math.cos(first.latitude * radians) * Math.cos(second.latitude * radians) *
    Math.sin(longitudeDelta / 2) ** 2;
  return 6371.0088 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

export function visibleTiles(center: Coordinates, zoom: number, width: number, height: number) {
  if (width <= 0 || height <= 0) return [];
  const world = toWorldPixels(center, zoom);
  const minX = Math.floor((world.x - width / 2) / tileSize);
  const maxX = Math.floor((world.x + width / 2) / tileSize);
  const minY = Math.floor((world.y - height / 2) / tileSize);
  const maxY = Math.floor((world.y + height / 2) / tileSize);
  const limit = 2 ** zoom;
  const tiles: {x:number;y:number;left:number;top:number}[] = [];
  for (let x = minX; x <= maxX; x++) for (let y = minY; y <= maxY; y++) {
    if (x < 0 || y < 0 || x >= limit || y >= limit) continue;
    tiles.push({x,y,left:x * tileSize - world.x + width / 2,top:y * tileSize - world.y + height / 2});
  }
  return tiles;
}
