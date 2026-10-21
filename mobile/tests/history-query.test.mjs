import test from "node:test";
import assert from "node:assert/strict";
import { compile, hooks } from "./helpers/compile.mjs";
const query = compile("../../../shared/history-query.ts");
const { defaultHistoryFilters } = compile("../../../shared/history.ts");
const tick = async () => { for(let i=0;i<15;i++)await Promise.resolve(); };

test("native history requests filters from the API and hides data immediately across accounts", async () => {
  const state = hooks(), calls = []; let poll, finish;
  const { useHistoryPage } = compile("../../src/use-history-page.ts", {
    react: state.react, "../../shared/history-query": query,
    "./use-foreground-poll": { useForegroundPoll: fn => { poll=fn; } },
    "./api": { request: async (path, token, method, body, signal) => {
      calls.push({path,token,method,signal});
      if(token==="second") return new Promise(resolve=>{finish=resolve;});
      return { ...query.emptyHistoryPage(), total: 160 };
    } },
  });
  const render = (token="first", page=1) => { state.reset(); return useHistoryPage(token, { ...defaultHistoryFilters, period: "6m" }, page, ""); };
  assert.equal(render().data, null); await poll(new AbortController().signal);
  assert.equal(render().data.total, 160); assert.match(calls[0].path, /period=6m/); assert.equal(calls[0].method, "GET");
  assert.equal(render("second").data, null);
  const pending=poll(new AbortController().signal); await tick(); const finishOld=finish;
  assert.equal(render("second", 2).data, null);
  const next=poll(new AbortController().signal); await tick();
  assert.equal(calls[1].signal.aborted, true); finish({ ...query.emptyHistoryPage(), total: 5 }); await next;
  assert.equal(render("second", 2).data.total, 5);
  finishOld({ ...query.emptyHistoryPage(), total: 999 }); await pending;
  assert.equal(render("second", 2).data.total, 5);
});
