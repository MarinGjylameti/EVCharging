import { readFileSync, mkdirSync, writeFileSync, existsSync } from "node:fs";
import { createRequire, Module } from "node:module";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import ts from "typescript";

const webRoot = fileURLToPath(new URL("../../", import.meta.url));
const compiledModules = [];

// Compile the actual production source. Named modules and inline source maps
// let Node's coverage collector report original TS/TSX lines, including JSX.
// Dependency overrides keep hook/Next tests in-process; they are not browser tests.
export function loadTypescript(sourceUrl, dependencies = {}) {
  const source = fileURLToPath(sourceUrl);
  // Avoid brackets in generated script names: Node's coverage lookup otherwise
  // misses the inline map for Next dynamic route directories such as [id].
  const generated = resolve(webRoot, ".local/test-modules", relative(webRoot, source).replace(/[\[\]]/g, "_") + ".cjs");
  const output = ts.transpileModule(readFileSync(source, "utf8"), {
    fileName: source,
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
      jsx: ts.JsxEmit.ReactJSX, sourceMap: true, inlineSources: true },
  });
  const map = JSON.parse(output.sourceMapText);
  map.sources = [pathToFileURL(source).href];
  const code = output.outputText.replace(/\/\/# sourceMappingURL=.*/g, "")
    + "\n//# sourceMappingURL=data:application/json;base64," + Buffer.from(JSON.stringify(map)).toString("base64");
  mkdirSync(dirname(generated), { recursive: true });
  if (!existsSync(generated) || readFileSync(generated, "utf8") !== code) writeFileSync(generated, code);
  const required = createRequire(source);
  const module = new Module(generated);
  module.filename = generated;
  module.require = name => Object.hasOwn(dependencies, name) ? dependencies[name] : required(name);
  module._compile(code, generated);
  compiledModules.push(module); // Keep the source-map cache alive through coverage collection.
  return module.exports;
}
