import type { EditorRuntime, RuntimeEvent, RuntimeListener } from './types.ts';

interface BrowserExports {
  Initialize(): Promise<void>;
  Invoke(operation: string, payloadJson: string): Promise<string>;
  Dispose(): Promise<void>;
}
interface WasmHost {
  exports: BrowserExports;
  listener?: RuntimeListener;
}

// Keep the .NET runtime alive across React StrictMode remounts and initialization retries.
const hosts = new Map<string, Promise<WasmHost>>();
async function loadHost(baseUrl: string): Promise<WasmHost> {
  const existing = hosts.get(baseUrl);
  if (existing) return existing;
  const pending = (async () => {
    const { dotnet } = await import(/* @vite-ignore */ `${baseUrl}_framework/dotnet.js`);
    const runtime = await dotnet.create();
    const host = {} as WasmHost;
    runtime.setModuleImports('runtime-events', {
      emit: (json: string) => {
        try { host.listener?.(JSON.parse(json) as RuntimeEvent); }
        catch (error) {
          host.listener?.({ type: 'RuntimeError', error: error instanceof Error ? error.message : String(error) });
        }
      },
    });
    const exports = await runtime.getAssemblyExports('FSMDemo.Browser');
    host.exports = exports.FSMDemo.Browser.BrowserExports;
    return host;
  })();
  hosts.set(baseUrl, pending);
  try { return await pending; }
  catch (error) { hosts.delete(baseUrl); throw error; }
}

/** baseUrl is the published frontend directory, e.g. /AnotherFSM/. */
export function createWasmRuntime(baseUrl = import.meta.env?.BASE_URL ?? '/'): EditorRuntime {
  const directory = baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`;
  const listeners = new Set<RuntimeListener>();
  const emit: RuntimeListener = (event) => listeners.forEach((listener) => listener(event));
  let host: WasmHost | undefined;
  let initialized = false;
  let lifecycle = Promise.resolve();
  const enqueue = (action: () => Promise<void>) => {
    const result = lifecycle.then(action);
    lifecycle = result.catch(() => {});
    return result;
  };
  async function invoke<T>(operation: string, payload: object = {}): Promise<T> {
    if (!initialized || !host) throw new Error('WASM runtime is not initialized.');
    const result = JSON.parse(await host.exports.Invoke(operation, JSON.stringify(payload)));
    if ('error' in result) throw new Error(result.error);
    // C# represents void as value:null; the common TypeScript contract represents it as void.
    return (result.value === null ? undefined : result.value) as T;
  }
  return {
    initialize: () => enqueue(async () => {
      if (initialized) return;
      host = await loadHost(directory);
      host.listener = emit;
      try {
        await host.exports.Initialize();
        initialized = true;
      } catch (error) {
        host.listener = undefined;
        await host.exports.Dispose();
        throw error;
      }
    }),
    subscribe: (listener) => { listeners.add(listener); return () => { listeners.delete(listener); }; },
    dispose: () => enqueue(async () => {
      initialized = false;
      if (host?.listener === emit) {
        host.listener = undefined;
        await host.exports.Dispose();
      }
    }),
    getStatus: () => invoke('getStatus'),
    exportScript: () => invoke('exportScript'),
    getModuleInstances: () => invoke('getModuleInstances'),
    getModuleDefinitions: () => invoke('getModuleDefinitions'),
    createModuleInstance: (req) => invoke('createModuleInstance', req),
    deleteModuleInstance: (instanceName) => invoke('deleteModuleInstance', { instanceName }),
    updateModuleInstancePosition: (instanceName, posX, posY) => invoke('updateModuleInstancePosition', { instanceName, posX, posY }),
    importScript: (script) => invoke('importScript', { script }),
    getNodes: () => invoke('getNodes'),
    getNode: (name) => invoke('getNode', { name }),
    createNode: (req) => invoke('createNode', req),
    deleteNode: (name) => invoke('deleteNode', { name }),
    renameNode: (name, newName) => invoke('renameNode', { name, newName }),
    updateNodePosition: (name, posX, posY) => invoke('updateNodePosition', { name, posX, posY }),
    updateNodeEvent: (name, index, newEventName) => invoke('updateNodeEvent', { name, index, newEventName }),
    getConnections: () => invoke('getConnections'),
    createConnection: (req) => invoke('createConnection', req),
    deleteConnection: (fromNodeName, toNodeName, eventName) => invoke('deleteConnection', { fromNodeName, toNodeName, eventName }),
    renameConnection: (fromNodeName, toNodeName, newEventName, eventName) => invoke('renameConnection', { fromNodeName, toNodeName, eventName, newEventName }),
    getExecutionStatus: () => invoke('getExecutionStatus'),
    startExecution: (startNodeName, endEventName) => invoke('startExecution', { startNodeName, endEventName }),
    pauseExecution: () => invoke('pauseExecution'),
    continueExecution: () => invoke('continueExecution'),
    stopExecution: () => invoke('stopExecution'),
  };
}
