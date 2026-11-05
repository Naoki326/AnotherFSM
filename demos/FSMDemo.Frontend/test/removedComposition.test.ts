import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createApiRuntime } from '../src/runtime/apiRuntime.ts';
import { createWasmRuntime } from '../src/runtime/wasmRuntime.ts';

for (const [mode, create] of [['api', () => createApiRuntime('http://localhost:5079')], ['wasm', () => createWasmRuntime()]] as const) {
  test(`${mode}: EditorRuntime no longer offers runtime composition editing`, () => {
    const runtime = create();
    assert.equal('updateGroupDefs' in runtime, false);
  });
}
