import test from "node:test";
import assert from "node:assert/strict";
import { albanianCities } from "../src/lib/albanian-cities.ts";

test("city selector covers the 61 Albanian municipalities without duplicates", () => {
  assert.equal(albanianCities.length,61);
  assert.equal(new Set(albanianCities).size,61);
  assert.ok(albanianCities.includes("Durrës"));
  assert.ok(albanianCities.includes("Tiranë"));
  assert.ok(albanianCities.includes("Dimal"));
});
