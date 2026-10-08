import { readFileSync } from "node:fs";
import { createRequire, Module } from "node:module";
import { fileURLToPath } from "node:url";
const required = createRequire(import.meta.url);
let ts;
try { ts = required("typescript"); }
catch (error) {
  if (error.code !== "MODULE_NOT_FOUND") throw error;
  // Offline workspace verification can use the already installed web compiler.
  ts = createRequire(new URL("../../../web/package.json", import.meta.url))("typescript");
}
export function compile(path, dependencies = {}) {
  const source = fileURLToPath(new URL(path, import.meta.url));
  const output = ts.transpileModule(readFileSync(source, "utf8"), {
    fileName: source, reportDiagnostics: true,
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
  });
  const errors = output.diagnostics?.filter(item => item.category === ts.DiagnosticCategory.Error) ?? [];
  if (errors.length) throw new Error(ts.formatDiagnosticsWithColorAndContext(errors, { getCanonicalFileName: file => file, getCurrentDirectory: () => "", getNewLine: () => "\n" }));
  const localRequire = createRequire(source), module = new Module(source + ".cjs");
  module.filename = source + ".cjs";
  module.require = name => Object.hasOwn(dependencies, name) ? dependencies[name] : localRequire(name);
  module._compile(output.outputText, module.filename);
  return module.exports;
}
export const jsx = { jsx: (type, props, key) => ({ type, props, key }), jsxs: (type, props, key) => ({ type, props, key }), Fragment: "Fragment" };
export function syntaxErrors(sourceUrl) {
  const source = fileURLToPath(sourceUrl);
  return ts.transpileModule(readFileSync(source, "utf8"), { fileName: source, reportDiagnostics: true, compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).diagnostics?.filter(item => item.category === ts.DiagnosticCategory.Error) ?? [];
}
export const nodes = (tree, predicate) => !tree || typeof tree !== "object" ? [] : [...(predicate(tree) ? [tree] : []), ...[tree.props?.children].flat(10).flatMap(child => nodes(child, predicate))];
export function hooks() {
  const values = []; let index = 0;
  return { reset: () => { index = 0; }, react: {
    useMemo: fn => fn(), useEffect() {}, useCallback: fn => fn,
    useRef(initial) { const i = index++; values[i] ??= { current: initial }; return values[i]; },
    useState(initial) { const i = index++; if (!(i in values)) values[i] = typeof initial === "function" ? initial() : initial; return [values[i], next => { values[i] = typeof next === "function" ? next(values[i]) : next; }]; },
  } };
}
