import test from "node:test";
import assert from "node:assert/strict";
import { readdirSync } from "node:fs";
import { syntaxErrors } from "./helpers/compile.mjs";

test("all mobile TS/TSX production sources transpile (syntax check, not native typecheck)", () => {
  const root = new URL("../", import.meta.url);
  const files = ["App.tsx", "app.config.ts", ...readdirSync(new URL("src/", root)).filter(file => /\.(ts|tsx)$/.test(file)).map(file => `src/${file}`)];
  for (const file of files) assert.deepEqual(syntaxErrors(new URL(file, root)), [], file);
  assert.ok(files.length >= 18);
});
