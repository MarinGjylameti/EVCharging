import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

const required = createRequire(import.meta.url);
const source = readFileSync(new URL("../scripts/upgrade-expo-go.cjs", import.meta.url), "utf8");

function workflow({ initial = 55, failTypecheck = false, plan = false } = {}) {
  let sdk = initial;
  const commands = [], writes = [], errors = [];
  const process = { argv: ["node", "upgrade.cjs", "--start", ...(plan ? ["--plan"] : [])], execPath: "node", env: {}, exitCode: undefined };
  const dependencies = {
    "node:fs": {
      existsSync: () => true,
      readFileSync: () => JSON.stringify({ version: `${sdk}.0.0` }),
      mkdirSync: path => writes.push(path),
      copyFileSync: (from, to) => writes.push([from, to]),
      readdirSync: () => ["api.test.mjs", "helpers"],
    },
    "node:child_process": {
      spawnSync: (_exe, args) => {
        commands.push(args);
        const upgrade = args.find(arg => /^expo@\^\d+/.test(arg));
        if (upgrade) sdk = Number(upgrade.match(/\d+/)[0]);
        return { status: failTypecheck && args[0].includes("typescript") ? 1 : 0 };
      },
    },
  };
  vm.runInNewContext(source, {
    __dirname: fileURLToPath(new URL("../scripts/", import.meta.url)), process,
    require: name => dependencies[name] ?? required(name),
    console: { log() {}, error: message => errors.push(message) },
  });
  return { commands, writes, errors, exitCode: process.exitCode };
}

test("resuming an interrupted SDK first aligns and verifies it before advancing or opening a tunnel", () => {
  const result = workflow();
  assert.equal(result.exitCode, undefined);
  assert.deepEqual(Array.from(result.commands[0]), ["node_modules/expo/bin/cli", "install", "--fix"]);
  const upgrades = result.commands.flat().filter(arg => arg.startsWith("expo@"));
  assert.deepEqual(upgrades, ["expo@^56.0.0", "expo@^57.0.0"]);
  assert.ok(result.commands[2][0].includes("typescript"));
  assert.ok(result.commands.at(-2).includes("export"));
  assert.ok(result.commands.at(-1).includes("--tunnel"));
});

test("a failed current-SDK check stops later upgrades, exports and tunnel startup", () => {
  const result = workflow({ failTypecheck: true });
  assert.equal(result.exitCode, 1);
  assert.equal(result.commands.length, 3);
  assert.equal(result.errors.length, 1);
  assert.ok(!result.commands.flat().some(arg => arg.startsWith("expo@") || arg === "export" || arg === "--tunnel"));
});

test("a preview plan does not install, write backups, export or open a tunnel", () => {
  const result = workflow({ plan: true });
  assert.equal(result.exitCode, undefined);
  assert.equal(result.commands.length, 0);
  assert.equal(result.writes.length, 0);
});
