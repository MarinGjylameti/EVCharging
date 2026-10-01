import test from "node:test";
import assert from "node:assert/strict";
import { coordinateAtPixel, distanceKm, fromWorldPixels, pinchMapView, toWorldPixels, visibleTiles, zoomAroundPixel } from "../src/lib/map-geometry.ts";

test("map projection returns the clicked geographic point", () => {
  const center = {latitude:41.3275,longitude:19.8187};
  const target = {latitude:41.3136,longitude:19.4556};
  const zoom = 13, width = 900, height = 540;
  const worldCenter = toWorldPixels(center,zoom);
  const worldTarget = toWorldPixels(target,zoom);
  const selected = coordinateAtPixel(center,zoom,width,height,
    worldTarget.x-worldCenter.x+width/2,worldTarget.y-worldCenter.y+height/2);
  assert.ok(Math.abs(selected.latitude-target.latitude)<1e-6);
  assert.ok(Math.abs(selected.longitude-target.longitude)<1e-6);
});

test("dragging a map right shifts its center west", () => {
  const center = {latitude:41.3275,longitude:19.8187};
  const world = toWorldPixels(center,16);
  const shifted = fromWorldPixels({x:world.x-100,y:world.y},16);
  assert.ok(shifted.longitude<center.longitude);
});

test("mouse wheel zoom keeps the geographic point under the cursor", () => {
  const center = {latitude:41.3136,longitude:19.4556};
  const x = 710, y = 140, width = 900, height = 540;
  const before = coordinateAtPixel(center,16,width,height,x,y);
  const afterCenter = zoomAroundPixel(center,16,17,width,height,x,y);
  const after = coordinateAtPixel(afterCenter,17,width,height,x,y);
  assert.ok(Math.abs(after.latitude-before.latitude)<1e-6);
  assert.ok(Math.abs(after.longitude-before.longitude)<1e-6);
});

test("pinch zoom keeps its geographic anchor under moving fingers", () => {
  const start = {center:{latitude:41.3275,longitude:19.8187},zoom:12};
  const width = 390, height = 400;
  const initial = {x:180,y:190}, moved = {x:210,y:215};
  const anchor = coordinateAtPixel(start.center,start.zoom,width,height,initial.x,initial.y);
  const result = pinchMapView(start,width,height,initial,moved,2);
  const after = coordinateAtPixel(result.center,result.zoom,width,height,moved.x,moved.y);
  assert.equal(result.zoom,13);
  assert.ok(Math.abs(after.latitude-anchor.latitude)<1e-6);
  assert.ok(Math.abs(after.longitude-anchor.longitude)<1e-6);
});

test("nearby stations can be ordered by distance from the driver", () => {
  const driver = {latitude:41.3136,longitude:19.4556};
  const durres = {latitude:41.3128,longitude:19.4487};
  const tirana = {latitude:41.3275,longitude:19.8187};
  assert.equal(distanceKm(driver,driver),0);
  assert.ok(distanceKm(driver,durres)<1);
  assert.ok(distanceKm(driver,durres)<distanceKm(driver,tirana));
});

test("tile list contains only tiles intersecting the viewport", () => {
  const center = {latitude:41.3275,longitude:19.8187};
  const tiles = visibleTiles(center,16,700,450);
  assert.ok(tiles.length>0 && tiles.length<=20);
  assert.ok(tiles.every(tile=>tile.x>=0&&tile.y>=0&&tile.x<2**16&&tile.y<2**16));
  assert.deepEqual(visibleTiles(center,16,0,450),[]);
});

test("selection stays within geographic bounds when panning past the world edge", () => {
  const selected = fromWorldPixels({x:-1000,y:-1000},8);
  assert.equal(selected.longitude,-180);
  assert.ok(selected.latitude<=85.051129);
});
