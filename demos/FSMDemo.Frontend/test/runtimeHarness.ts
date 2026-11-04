import { createApiRuntime } from '../src/runtime/apiRuntime';
import { createWasmRuntime } from '../src/runtime/wasmRuntime';

const mode = new URLSearchParams(location.search).get('mode') || 'wasm';
const runtime = mode === 'api' ? createApiRuntime('http://127.0.0.1:5079') : createWasmRuntime();
Object.assign(window, { contractRuntime: runtime, contractEvents: [] });
runtime.subscribe(event => {
  (window as unknown as { contractEvents: unknown[] }).contractEvents.push(event);
  document.querySelector('#result')!.textContent = JSON.stringify({ mode, event }, null, 2);
});
try {
  await runtime.initialize();
  document.querySelector('#result')!.textContent = JSON.stringify({ mode, ready: true });
  Object.assign(window, { contractReady: true });
} catch (error) {
  document.querySelector('#result')!.textContent = String(error);
}
