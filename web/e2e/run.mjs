// Only dedicated npm commands reference this entry point. Never uses npx, installs
// dependencies/browsers, starts servers or imports Playwright during ordinary work.
import { existsSync } from 'node:fs';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { e2eEnvironment } from './environment.mjs';

const args = process.argv.slice(2);
const smoke = args.length === 1 && args[0] === '--smoke';
try {
  if (args.length && !smoke) throw new Error('Supported E2E commands: test:e2e and test:e2e:smoke.');
  if (Number(process.versions.node.split('.')[0]) < 22) throw new Error('E2E requires an approved Node.js 22+ environment.');
  e2eEnvironment(process.env, { charging: !smoke, requireCommand: false });
  const cli = fileURLToPath(new URL('./node_modules/@playwright/test/cli.js', import.meta.url));
  if (!existsSync(cli)) throw new Error('Optional E2E dependencies are not installed. Follow docs/e2e.md later in an approved environment; nothing was installed or started.');
  const child = spawn(process.execPath, [cli, 'test', '--config', fileURLToPath(new URL('./playwright.config.mjs', import.meta.url)),
    ...(smoke ? ['--grep', '@smoke'] : [])], {
    cwd: fileURLToPath(new URL('.', import.meta.url)), stdio: 'inherit', shell: false,
    env: { ...process.env, EVCHARGING_EXPLICIT_E2E: '1' }
  });
  child.once('error', () => { process.stderr.write('Unable to start the explicitly requested E2E runner.\n'); process.exitCode = 1; });
  child.once('exit', code => { process.exitCode = code ?? 1; });
  for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => child.kill(signal));
} catch (error) {
  process.stderr.write(`${error.message}\n`);
  process.exitCode = 1;
}
