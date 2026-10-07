// Static configuration checks only. Never import Playwright, its config, an OCPP
// fixture or the E2E entry point; normal tests must not launch the optional suite.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { e2eEnvironment } from '../e2e/environment.mjs';

const valid = {
  EVCHARGING_EXPLICIT_E2E: '1', E2E_ENVIRONMENT: 'staging', E2E_RESOURCE_ACK: 'dedicated-test-resources',
  E2E_BASE_URL: 'https://e2e-web.example.test', E2E_API_URL: 'https://e2e-api.example.test',
  E2E_APPROVED_ORIGINS: 'https://e2e-web.example.test,https://e2e-api.example.test',
  E2E_DRIVER_EMAIL: 'e2e-driver@example.test', E2E_DRIVER_PASSWORD: 'synthetic-e2e-password',
  E2E_CHARGER_ID: 'SIM-E2E-001', E2E_CHARGER_PASSWORD: 'synthetic-e2e-password', E2E_TAG_ID: 'TAG-001'
};

test('optional E2E requires an explicit command and every approved environment setting', () => {
  assert.equal(e2eEnvironment(valid, { charging: true }).identity, 'SIM-E2E-001');
  for (const key of ['EVCHARGING_EXPLICIT_E2E', 'E2E_ENVIRONMENT', 'E2E_RESOURCE_ACK', 'E2E_BASE_URL', 'E2E_API_URL', 'E2E_APPROVED_ORIGINS', 'E2E_DRIVER_EMAIL', 'E2E_DRIVER_PASSWORD', 'E2E_CHARGER_ID', 'E2E_CHARGER_PASSWORD', 'E2E_TAG_ID']) {
    assert.throws(() => e2eEnvironment({ ...valid, [key]: '' }, { charging: true }), { name: 'Error' });
  }
  assert.throws(() => e2eEnvironment({ ...valid, E2E_ENVIRONMENT: 'production' }));
  assert.throws(() => e2eEnvironment({ ...valid, E2E_RESOURCE_ACK: 'yes' }));
});

test('E2E rejects production, remote insecure, unapproved and ambiguous URLs without contacting them', () => {
  for (const url of ['https://prod.example.test', 'https://production.example.test', 'https://api.example.com',
    'http://staging.example.test', 'https://secret:password@e2e.example.test', 'https://e2e.example.test/path',
    'https://e2e.example.test/?password=secret', 'https://e2e.example.test/#fragment', 'not-a-url', 'file:///tmp/test']) {
    assert.throws(() => e2eEnvironment({ ...valid, E2E_API_URL: url, E2E_APPROVED_ORIGINS: `${valid.E2E_BASE_URL},${url}` }));
  }
  assert.throws(() => e2eEnvironment({ ...valid, E2E_APPROVED_ORIGINS: 'https://*.example.test' }));
  assert.throws(() => e2eEnvironment({ ...valid, E2E_ENVIRONMENT: 'local' }));
  const local = { ...valid, E2E_ENVIRONMENT: 'local', E2E_BASE_URL: 'http://127.0.0.1:3100', E2E_API_URL: 'http://127.0.0.1:5201', E2E_APPROVED_ORIGINS: 'http://127.0.0.1:3100,http://127.0.0.1:5201' };
  assert.equal(e2eEnvironment(local).baseUrl, local.E2E_BASE_URL);
});

test('E2E requires dedicated accounts and charger identities; smoke does not require a charger', () => {
  const smoke = { ...valid, E2E_CHARGER_ID: '', E2E_CHARGER_PASSWORD: '', E2E_TAG_ID: '' };
  assert.equal(e2eEnvironment(smoke).identity, undefined);
  for (const identity of ['SIM-001', 'REAL-CHARGER', 'SIM-E2E-', 'SIM-E2E-!']) assert.throws(() => e2eEnvironment({ ...valid, E2E_CHARGER_ID: identity }, { charging: true }));
  assert.throws(() => e2eEnvironment({ ...valid, E2E_DRIVER_EMAIL: 'driver@real.example' }));
  assert.throws(() => e2eEnvironment({ ...valid, E2E_DRIVER_PASSWORD: 'replace-me-before-running' }));
  assert.throws(() => e2eEnvironment({ ...valid, E2E_CHARGER_PASSWORD: 'replace-me-before-running' }, { charging: true }));
  assert.throws(() => e2eEnvironment({ ...valid, E2E_TAG_ID: 'x'.repeat(21) }, { charging: true }));
  const secretWithSpaces = ' synthetic-e2e-password ';
  const settings = e2eEnvironment({ ...valid, E2E_DRIVER_PASSWORD: secretWithSpaces, E2E_CHARGER_PASSWORD: secretWithSpaces }, { charging: true });
  assert.equal(settings.password, secretWithSpaces);
  assert.equal(settings.chargerPassword, secretWithSpaces);
});

test('ordinary scripts stay separate from E2E dependencies, specs and server startup', () => {
  const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
  const pkg = JSON.parse(read('../package.json'));
  assert.equal(pkg.scripts.test, 'node --test tests/*.test.mjs');
  assert.equal(pkg.scripts.build, 'node scripts/next-local.cjs build');
  assert.equal(pkg.scripts.typecheck, 'tsc --noEmit');
  for (const [name, command] of Object.entries(pkg.scripts)) if (!name.startsWith('test:e2e')) assert.doesNotMatch(command, /e2e|playwright/i);
  assert.equal(pkg.scripts['test:e2e'], 'node e2e/run.mjs');
  assert.equal(pkg.scripts['test:e2e:smoke'], 'node e2e/run.mjs --smoke');
  assert.equal(pkg.devDependencies['@playwright/test'], undefined);
  const optional = JSON.parse(read('../e2e/package.json'));
  assert.ok(optional.devDependencies['@playwright/test']);
  assert.ok(optional.devDependencies.ws);
  assert.equal(optional.scripts, undefined); // No install/start/aggregate hooks.
  assert.ok(JSON.parse(read('../tsconfig.json')).exclude.includes('e2e'));
  assert.equal(readdirSync(new URL('../e2e/specs/', import.meta.url)).filter(name => name.endsWith('.test.mjs')).length, 0);
  assert.doesNotMatch(read('../e2e/playwright.config.mjs'), /^\s*(webServer|globalSetup|globalTeardown)\s*:/m);
  assert.doesNotMatch(read('../e2e/run.mjs'), /['"]npx['"]|playwright install|npm install|npm ci/);
  assert.doesNotMatch(read('../../.github/workflows/tests.yml'), /e2e|playwright/i);
});
