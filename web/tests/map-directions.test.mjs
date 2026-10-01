import test from "node:test";
import assert from "node:assert/strict";
import { directionsUrl, mapProviderFromPreference } from "../src/lib/map-directions.ts";

test("Google Maps is the default and Apple Maps needs an explicit preference", () => {
  assert.equal(mapProviderFromPreference(null), "google");
  assert.equal(mapProviderFromPreference("auto"), "google");
  assert.equal(mapProviderFromPreference("google"), "google");
  assert.equal(mapProviderFromPreference("apple"), "apple");
});

test("directions use the exact selected station coordinates and driving mode", () => {
  const destination = {latitude: 41.3211, longitude: 19.8174};
  const google = new URL(directionsUrl("google", destination));
  assert.equal(google.origin, "https://www.google.com");
  assert.equal(google.searchParams.get("destination"), "41.321100,19.817400");
  assert.equal(google.searchParams.get("travelmode"), "driving");
  assert.equal(google.searchParams.get("dir_action"), "navigate");
  const apple = new URL(directionsUrl("apple", destination));
  assert.equal(apple.origin, "https://maps.apple.com");
  assert.equal(apple.searchParams.get("daddr"), "41.321100,19.817400");
  assert.equal(apple.searchParams.get("dirflg"), "d");
});

test("invalid map coordinates are rejected", () => {
  assert.throws(() => directionsUrl("google", {latitude: Number.NaN, longitude: 19.8}), RangeError);
  assert.throws(() => directionsUrl("apple", {latitude: 95, longitude: 19.8}), RangeError);
});
