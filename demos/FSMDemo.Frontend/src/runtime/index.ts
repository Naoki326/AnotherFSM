import { createApiRuntime } from './apiRuntime.ts';
import { createWasmRuntime } from './wasmRuntime.ts';

export const runtimeMode = import.meta.env.VITE_RUNTIME_MODE ?? 'api';
export const runtimeConfigurationError = runtimeMode === 'api' || runtimeMode === 'wasm'
  ? null : `Unsupported VITE_RUNTIME_MODE: ${runtimeMode}. Expected api or wasm.`;

// Selection happens exactly once at the application boundary, never in components or stores.
export const runtime = runtimeMode === 'wasm'
  ? createWasmRuntime()
  : createApiRuntime(import.meta.env.VITE_API_URL || 'http://localhost:5079');
