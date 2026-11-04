// Compatibility export for consumers of the old client. All operations now share the runtime contract.
export { runtime as api } from '../runtime/index.ts';
export type { EditorRuntime, RuntimeEvent } from '../runtime/types.ts';
