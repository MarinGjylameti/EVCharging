import { mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { relative, resolve } from "node:path";

const root = fileURLToPath(new URL("../", import.meta.url));
const output = resolve(root, "../.local/coverage/web");
mkdirSync(output, { recursive: true });
const tests = readdirSync(resolve(root, "tests")).filter(name => name.endsWith(".test.mjs")).sort().map(name => `tests/${name}`);
const result = spawnSync(process.execPath, ["--enable-source-maps", "--experimental-test-coverage", "--test",
  "--test-reporter=spec", `--test-reporter-destination=${output}/tests.txt`,
  "--test-reporter=lcov", `--test-reporter-destination=${output}/lcov.info`, ...tests], { cwd: root, stdio: "inherit" });
if (result.error) throw result.error;
if (result.status !== 0) {
  process.stderr.write(readFileSync(resolve(output, "tests.txt"), "utf8"));
  process.exit(result.status ?? 1);
}

// Native coverage includes only loaded files. Keep the untouched source inventory
// visible and exclude test helpers from the reported production percentages.
const files = new Map();
for (const record of readFileSync(resolve(output, "lcov.info"), "utf8").split("end_of_record")) {
  const raw = /^SF:(.+)$/m.exec(record)?.[1]?.trim().replaceAll("\\", "/");
  if (!raw) continue;
  const path = raw.startsWith("src/") ? raw : relative(root, raw).replaceAll("\\", "/");
  if (!path.startsWith("src/") || !/\.tsx?$/.test(path)) continue;
  const file = files.get(path) ?? { path, lines: new Map(), branches: new Map(), functions: new Map() };
  for (const [, line, count] of record.matchAll(/^DA:(\d+),(\d+)/gm)) file.lines.set(line, Math.max(file.lines.get(line) ?? 0, Number(count)));
  for (const [, key, count] of record.matchAll(/^BRDA:([^\r\n]+),([^,\r\n]+)$/gm)) file.branches.set(key, Math.max(file.branches.get(key) ?? 0, count === "-" ? 0 : Number(count)));
  for (const [, count, name] of record.matchAll(/^FNDA:(\d+),(.+)$/gm)) file.functions.set(name.trim(), Math.max(file.functions.get(name.trim()) ?? 0, Number(count)));
  files.set(path, file);
}
if (!files.size) throw new Error("No production source coverage was collected.");
const percent = (hit, total) => total ? Number((100 * hit / total).toFixed(2)) : null;
const counts = entries => ({ total: entries.size, hit: [...entries.values()].filter(count => count > 0).length });
const summary = [...files.values()].sort((a, b) => a.path.localeCompare(b.path)).map(file => {
  const lines = counts(file.lines), branches = counts(file.branches), functions = counts(file.functions);
  return { path: file.path, lines, branches, functions, linePercent: percent(lines.hit, lines.total), branchPercent: percent(branches.hit, branches.total) };
});
const totals = Object.fromEntries(["lines", "branches", "functions"].map(kind => [kind, summary.reduce((sum, file) => ({ hit: sum.hit + file[kind].hit, total: sum.total + file[kind].total }), { hit: 0, total: 0 })]));
function inventory(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = resolve(directory, entry.name);
    return entry.isDirectory() ? inventory(path) : /\.tsx?$/.test(entry.name) && !entry.name.endsWith(".d.ts") ? [relative(root, path).replaceAll("\\", "/")] : [];
  });
}
const source = inventory(resolve(root, "src")).sort();
const report = { scope: "Loaded production TS/TSX files only; unloaded files have no measured percentage.", sourceFiles: source.length,
  loadedFiles: summary.length, linePercent: percent(totals.lines.hit, totals.lines.total), branchPercent: percent(totals.branches.hit, totals.branches.total),
  totals, files: summary, unloadedFiles: source.filter(path => !files.has(path)) };
writeFileSync(resolve(output, "summary.json"), JSON.stringify(report, null, 2));
writeFileSync(resolve(output, "summary.md"), `# Web coverage\n\n${report.scope}\n\nLoaded ${report.loadedFiles}/${report.sourceFiles} source files. Lines ${report.linePercent}%; branches ${report.branchPercent}%.\n\n| File | Lines | Branches |\n|---|---:|---:|\n${summary.map(file => `| ${file.path} | ${file.linePercent}% | ${file.branchPercent ?? "n/a"}% |`).join("\n")}\n\n## Files outside this measurement\n\n${report.unloadedFiles.map(path => `- ${path}`).join("\n")}\n`);
process.stdout.write(`Web coverage: ${report.linePercent}% lines, ${report.branchPercent}% branches across ${report.loadedFiles}/${report.sourceFiles} production files.\nReports: ${output}\n`);
