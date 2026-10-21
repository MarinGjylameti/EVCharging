import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

const required = createRequire(import.meta.url);
const source = readFileSync(new URL("../scripts/build-apk.cjs", import.meta.url), "utf8");
const id = "11111111-2222-3333-4444-555555555555";
const settings = JSON.parse(readFileSync(new URL("../eas.json", import.meta.url), "utf8"));

function workflow({ linked = "", fail = "", returnedId = id, returnedSlug = "evcharging", plan = false, tempPath, realTempPath } = {}) {
  const commands = [], writes = [], errors = [], files = new Map();
  const process = { argv: ["node", "build-apk.cjs", ...(plan ? ["--plan"] : [])], execPath: "node", env: { npm_execpath: "npm-cli.js", ...(tempPath ? { EVCHARGING_EAS_TEMP_DIR: tempPath } : {}) }, exitCode: undefined, exit: code => { throw { exit: code }; } };
  const dependencies = {
    "../config/project-link.cjs": { linkedProjectId: () => linked },
    "../config/build-environment.cjs": required("../config/build-environment.cjs"),
    "node:fs": {
      realpathSync: path => path === tempPath && realTempPath ? realTempPath : path,
      existsSync: path => files.has(path),
      readFileSync: path => path.replaceAll("\\", "/").endsWith("expo/package.json") ? JSON.stringify({ version: "57.0.30" }) : path.endsWith("eas.json") ? JSON.stringify(settings) : files.get(path),
      mkdirSync: path => writes.push({ path, directory: true }),
      writeFileSync: (path, text, options) => { files.set(path, text); writes.push({ path, text, options }); },
      readdirSync: () => ["api.test.mjs", "helpers"],
    },
    "node:child_process": {
      spawnSync: (_exe, args, options) => {
        commands.push({ args: Array.from(args), ...options });
        if (fail && args.some(arg => arg.includes(fail))) return { status: 1 };
        if (args.includes("project:init")) {
          const file = required("node:path").join(options.cwd, "app.json");
          files.set(file, JSON.stringify({ expo: { slug: returnedSlug, extra: { eas: { projectId: returnedId } } } }));
        }
        return { status: 0 };
      },
    },
  };
  try {
    vm.runInNewContext(source, {
      __dirname: fileURLToPath(new URL("../scripts/", import.meta.url)), process,
      require: name => dependencies[name] ?? required(name),
      console: { log() {}, error: message => errors.push(message) },
    });
  } catch (error) { if (error.exit !== 0) throw error; }
  return { commands, writes, errors, exitCode: process.exitCode };
}

test("APK creation saves only EAS's returned project ID and builds the internal profile after checks", () => {
  const result = workflow();
  assert.equal(result.exitCode, undefined, result.errors.join("\n"));
  const saved = result.writes.find(item => item.path.endsWith("eas-project.json"));
  assert.equal(JSON.parse(saved.text).projectId, id); assert.equal(saved.options.flag, "wx");
  assert.ok(result.commands[0].args[0].includes("typescript"));
  assert.equal(result.commands[1].args[0], "--test");
  const build = result.commands.at(-1);
  assert.deepEqual(build.args.slice(1), ["build", "--platform", "android", "--profile", "preview-ui", "--wait"]);
  assert.equal(build.env.EAS_BUILD_PROFILE, "preview-ui");
  assert.equal(build.env.EXPO_PUBLIC_API_BASE_URL, settings.build["preview-ui"].env.EXPO_PUBLIC_API_BASE_URL);
  assert.ok(result.commands.at(-2).args.includes("export"));
});

test("EAS alone uses external staging, while compiler temporary files stay local", () => {
  const tempPath = required("node:path").resolve("C:/external-eas-temp");
  const result = workflow({ linked: id, tempPath });
  assert.equal(result.exitCode, undefined, result.errors.join("\n"));
  assert.equal(result.commands.at(-1).env.TEMP, tempPath);
  assert.equal(result.commands.at(-1).env.TMPDIR, tempPath);
  assert.notEqual(result.commands[0].env.TEMP, tempPath);
});

test("staging inside the repository or via a junction is rejected before any command/write", () => {
  const repository = fileURLToPath(new URL("../../", import.meta.url));
  const inside = required("node:path").resolve(repository, ".local/eas-temp");
  for (const options of [{ tempPath: repository }, { tempPath: inside }, { tempPath: required("node:path").resolve("C:/external-eas-temp"), realTempPath: inside }]) {
    const result = workflow(options);
    assert.equal(result.exitCode, 1); assert.match(result.errors[0], /outside the repository/);
    assert.equal(result.commands.length, 0); assert.equal(result.writes.length, 0);
  }
});

test("APK workflow reuses the linked project without initialization or rewriting its ID", () => {
  const result = workflow({ linked: id });
  assert.equal(result.exitCode, undefined);
  assert.ok(!result.commands.some(command => command.args.includes("project:init")));
  assert.ok(!result.writes.some(item => item.path.endsWith("eas-project.json")));
});

test("failed tests or Android export stop the build; bad EAS results are never saved", () => {
  const tests = workflow({ fail: "--test" });
  assert.equal(tests.exitCode, 1); assert.equal(tests.commands.length, 2);
  const exported = workflow({ linked: id, fail: "export" });
  assert.equal(exported.exitCode, 1);
  assert.ok(!exported.commands.some(command => command.args.includes("build")));
  for (const options of [{ returnedId: "invalid" }, { returnedSlug: "different-app" }, { fail: "project:init" }]) {
    const result = workflow(options);
    assert.equal(result.exitCode, 1);
    assert.ok(!result.writes.some(item => item.path.endsWith("eas-project.json")));
    assert.ok(!result.commands.some(command => command.args.includes("build")));
  }
});

test("APK plan performs no installs, initialization, writes or builds", () => {
  const result = workflow({ plan: true });
  assert.equal(result.commands.length, 0); assert.equal(result.writes.length, 0);
});
