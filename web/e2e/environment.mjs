// Pure validation only: no network, dependency loading, browsers or processes.
function required(env, name, trim = true) {
  if (!env[name]?.trim()) throw new Error(`${name} must be explicitly configured.`);
  return trim ? env[name].trim() : env[name];
}

function origin(value, name) {
  let url;
  try { url = new URL(value); } catch { throw new Error(`${name} must be an absolute origin.`); }
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password
    || url.pathname !== '/' || url.search || url.hash) {
    throw new Error(`${name} must be an HTTP(S) origin without credentials, path or query.`);
  }
  return url;
}

export function e2eEnvironment(env, { charging = false, requireCommand = true } = {}) {
  if (requireCommand && env.EVCHARGING_EXPLICIT_E2E !== '1') {
    throw new Error('E2E is opt-in. Use only the dedicated test:e2e or test:e2e:smoke command.');
  }
  const environment = required(env, 'E2E_ENVIRONMENT');
  if (!['local', 'ci', 'staging'].includes(environment)) throw new Error('E2E_ENVIRONMENT must be local, ci or staging; production is forbidden.');
  if (env.E2E_RESOURCE_ACK !== 'dedicated-test-resources') throw new Error('E2E_RESOURCE_ACK must confirm dedicated-test-resources.');
  const base = origin(required(env, 'E2E_BASE_URL'), 'E2E_BASE_URL');
  const api = origin(required(env, 'E2E_API_URL'), 'E2E_API_URL');
  const approved = required(env, 'E2E_APPROVED_ORIGINS').split(',').map(value => origin(value.trim(), 'E2E_APPROVED_ORIGINS').origin);
  for (const url of [base, api]) {
    if (!approved.includes(url.origin)) throw new Error('Both E2E origins must exactly match E2E_APPROVED_ORIGINS; wildcards are forbidden.');
    const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
    if (environment === 'local' && !loopback) throw new Error('Local E2E is restricted to explicit loopback origins.');
    if (/(^|[.-])prod(uction)?([.-]|$)/i.test(url.hostname)) throw new Error('Production hostnames are forbidden for this suite.');
    if (!loopback && (!/(^|[.-])(e2e|test|testing|staging|demo)([.-]|$)/i.test(url.hostname) || url.protocol !== 'https:')) {
      throw new Error('Remote E2E requires HTTPS and an explicitly approved test/staging/demo hostname.');
    }
  }
  const email = required(env, 'E2E_DRIVER_EMAIL');
  if (!/^e2e-[^@\s]+@[^@\s]+$/i.test(email)) throw new Error('Use a dedicated Driver account whose email starts with e2e-.');
  const password = required(env, 'E2E_DRIVER_PASSWORD', false);
  if (password.length < 12 || password.length > 128 || password.trim() === 'replace-me-before-running') throw new Error('Provide the dedicated test Driver password through the environment.');
  const settings = { environment, baseUrl: base.origin, apiUrl: api.origin, approvedOrigins: approved, email, password };
  if (charging) {
    settings.identity = required(env, 'E2E_CHARGER_ID');
    if (!/^SIM-E2E-[A-Z0-9_-]{1,24}$/.test(settings.identity)) throw new Error('Use a dedicated allowlisted SIM-E2E-* charger identity.');
    settings.chargerPassword = required(env, 'E2E_CHARGER_PASSWORD', false);
    if (settings.chargerPassword.length < 12 || settings.chargerPassword.length > 128
      || settings.chargerPassword.trim() === 'replace-me-before-running') throw new Error('Provide the test simulator password through the environment.');
    settings.tag = required(env, 'E2E_TAG_ID');
    if (settings.tag.length > 20) throw new Error('E2E_TAG_ID must match the existing simulator configuration (maximum 20 characters).');
  }
  return Object.freeze(settings);
}
