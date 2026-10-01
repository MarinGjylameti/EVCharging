// Copies selected existing npm cache entries into this project's cache. No network.
const path = require("node:path");
const fs = require("node:fs/promises");
const cacache = require("C:/Program Files/nodejs/node_modules/npm/node_modules/cacache");
const index = require("C:/Program Files/nodejs/node_modules/npm/node_modules/cacache/lib/entry-index.js");
const semver = require("C:/Program Files/nodejs/node_modules/npm/node_modules/semver");
async function main() {
  const root = path.resolve(__dirname, "..");
  const source = path.join(process.env.LOCALAPPDATA, "npm-cache", "_cacache");
  const dest = path.join(root, ".local", "npm", "_cacache");
  const entries = await cacache.ls(source);
  const byUrl = new Map(Object.values(entries).map(e => [e.key, e]));
  const seen = new Set();
  let copied = 0;
  async function copy(entry) {
    if (!entry || seen.has(entry.key)) return;
    const variants = await index.bucketEntries(index.bucketPath(source, entry.key));
    const digests = new Set();
    for (const variant of variants) {
      if (!variant.integrity || digests.has(variant.integrity)) continue;
      const data = await cacache.get.byDigest(source, variant.integrity);
      await cacache.put(dest, entry.key, data, { metadata: variant.metadata });
      digests.add(variant.integrity); copied += data.length;
    }
    seen.add(entry.key);
  }
  function metadataEntry(name) {
    return Object.values(entries).find(e => e.key.replace(/%2f/gi, "/").endsWith("registry.npmjs.org/" + name));
  }
  const selected = new Set();
  const missing = new Set();
  async function visit(name, range) {
    const entry = metadataEntry(name);
    if (!entry) { missing.add(name + "@" + range + " metadata"); return; }
    await copy(entry);
    const meta = JSON.parse((await cacache.get(source, entry.key)).data);
    const versions = Object.keys(meta.versions || {}).filter(v => semver.satisfies(v, range));
    const version = semver.maxSatisfying(versions, range);
    if (!version) { missing.add(name + "@" + range + " version"); return; }
    const id = name + "@" + version;
    if (selected.has(id)) return;
    selected.add(id);
    const pkg = meta.versions[version];
    const tar = byUrl.get("make-fetch-happen:request-cache:" + pkg.dist?.tarball);
    if (tar) await copy(tar); else missing.add(id + " tarball");
    for (const [dependency, requested] of Object.entries({...pkg.dependencies, ...pkg.optionalDependencies})) {
      if (dependency.startsWith("@next/swc-") && dependency !== "@next/swc-win32-x64-msvc") continue;
      if (dependency.startsWith("@img/") && !dependency.includes("win32-x64") && dependency !== "@img/colour") continue;
      await visit(dependency, requested);
    }
  }
  const manifest = JSON.parse(await fs.readFile(path.join(root, "web", "package.json"), "utf8"));
  for (const [name, range] of Object.entries({...manifest.dependencies, ...manifest.devDependencies})) await visit(name, range);
  console.log(JSON.stringify({copiedEntries:seen.size, megabytes:Math.round(copied/1024/1024), selected:[...selected], missing:[...missing]}, null, 2));
}
main().catch(e => { console.error(e.message); process.exitCode=1; });
