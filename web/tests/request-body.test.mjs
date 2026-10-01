import test from "node:test";
import assert from "node:assert/strict";
import { boundedJson } from "../src/lib/request-body.ts";

function request(body, type = "application/json") {
  return new Request("http://test.local", { method: "POST", headers: { "Content-Type": type }, body });
}
test("accepts an ordinary JSON object", async () => {
  assert.deepEqual(await boundedJson(request('{"email":"operator@example.com"}')), {email:"operator@example.com"});
});
test("rejects oversized streaming input regardless of content-length", async () => {
  await assert.rejects(boundedJson(request(JSON.stringify({input:"a".repeat(8192)}))), /body_too_large/);
});
test("rejects primitives, arrays, null and malformed JSON", async () => {
  for (const body of ["null", "[]", "42", "{"]) await assert.rejects(boundedJson(request(body)));
});
test("rejects a form submission without JSON content type", async () => {
  await assert.rejects(boundedJson(request("email=x", "application/x-www-form-urlencoded")), /invalid_body/);
});
