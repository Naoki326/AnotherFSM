import { spawn } from 'node:child_process';
import { openSync, closeSync, mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout } from 'node:timers/promises';
import { connect } from 'node:net';

const root = fileURLToPath(new URL('../', import.meta.url));
const frontend = resolve(root, 'demos/FSMDemo.Frontend');
const logs = resolve(root, '.scratch/issue-7');
mkdirSync(logs, { recursive: true });
const children = new Set();
const fds = [];
const start = (command, args, log, options = {}) => {
  const fd = openSync(resolve(logs, log), 'w');
  fds.push(fd);
  const child = spawn(command, args, { cwd: root, stdio: ['ignore', fd, fd], ...options });
  children.add(child);
  child.on('exit', () => children.delete(child));
  return child;
};
const exited = child => new Promise((resolve, reject) => {
  child.on('error', reject);
  child.on('exit', code => code === 0 ? resolve() : reject(new Error(`Process ${child.spawnargs.join(' ')} exited ${code}; see ${logs}`)));
});
async function listening(port) {
  return new Promise(resolve => {
    const socket = connect({ host: '127.0.0.1', port });
    const done = open => { socket.destroy(); resolve(open); };
    socket.setTimeout(1000);
    socket.once('connect', () => done(true));
    socket.once('error', () => done(false));
    socket.once('timeout', () => done(false));
  });
}
async function available(url) {
  try { return (await fetch(url, { signal: AbortSignal.timeout(1000) })).ok; } catch { return false; }
}
async function ready(url, child) {
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline && child.exitCode === null) {
    if (await available(url)) return;
    await setTimeout(100);
  }
  throw new Error(`Server did not become ready: ${url}; see ${logs}`);
}
const tests = pattern => exited(start(process.execPath,
  [resolve(frontend, 'node_modules/@playwright/test/cli.js'), 'test', '--grep', pattern],
  pattern.startsWith('api') ? 'api-browser.log' : 'wasm-browser.log', {
    cwd: frontend,
    env: { ...process.env, RUNTIME_TEST_REPORT: resolve(logs, pattern.startsWith('api') ? 'api-results.json' : 'wasm-results.json') },
  }));
try {
  if (await listening(5079))
    throw new Error('Port 5079 already has a business API. Stop it before backend-free WASM verification.');
  if (await listening(5180))
    throw new Error('Port 5180 is already serving. Stop it before running isolated browser tests.');
  const server = start(process.execPath, ['tools/serve-browser.mjs'], 'static.log');
  await ready('http://127.0.0.1:5180/AnotherFSM/', server);
  await tests('wasm:');
  await exited(start('dotnet', ['build', 'demos/FSMDemo.API/FSMDemo.API.csproj', '--nologo', '-p:NuGetAudit=false'], 'api-build.log'));
  const api = start('dotnet', ['demos/FSMDemo.API/bin/Debug/net10.0/FSMDemo.API.dll', '--urls', 'http://127.0.0.1:5079'], 'api.log');
  await ready('http://127.0.0.1:5079/api/engine', api);
  await tests('api:');
  console.log('Backend-free real WASM and API browser contracts passed. Logs: .scratch/issue-7/');
} catch (error) {
  console.error(error);
  process.exitCode = 1;
} finally {
  const remaining = [...children];
  await Promise.all(remaining.map(child => new Promise(resolve => {
    child.once('exit', resolve);
    child.kill();
  })));
  fds.forEach(closeSync);
}
