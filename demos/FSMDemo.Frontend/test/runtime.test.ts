import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createWasmRuntime } from '../src/runtime/wasmRuntime.ts';

test('WASM operations before initialization fail explicitly, never fall back to API', async () => {
  const runtime = createWasmRuntime('/AnotherFSM/');
  await assert.rejects(runtime.getStatus(), /not initialized/i);
  await runtime.dispose();
  await assert.rejects(runtime.exportScript(), /not initialized/i);
});
