import * as signalR from '@microsoft/signalr';
import type { EditorRuntime, RuntimeEvent, RuntimeListener } from './types.ts';

export function createApiRuntime(baseUrl = 'http://localhost:5079'): EditorRuntime {
  const origin = baseUrl.replace(/\/$/, '');
  const listeners = new Set<RuntimeListener>();
  let connection: signalR.HubConnection | undefined;
  let lifecycle = Promise.resolve();
  const enqueue = (action: () => Promise<void>) => {
    const result = lifecycle.then(action);
    lifecycle = result.catch(() => {});
    return result;
  };
  const emit = (event: RuntimeEvent) => listeners.forEach((listener) => listener(event));
  const report = (error: unknown) => emit({ type: 'RuntimeError', error: error instanceof Error ? error.message : String(error) });

  async function request<T>(path: string, method = 'GET', body?: unknown, text = false): Promise<T> {
    const response = await fetch(`${origin}/api${path}`, {
      method, headers: { 'Content-Type': 'application/json' },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    if (!response.ok) {
      const error = await response.json().catch(() => ({ error: response.statusText }));
      throw new Error(error.error || response.statusText || `HTTP ${response.status}`);
    }
    const content = await response.text();
    return (text ? content : content ? JSON.parse(content) : undefined) as T;
  }
  const namePath = (name: string) => `/nodes/${encodeURIComponent(name)}`;
  const modulePath = (name: string) => `/nodes/module-instance/${encodeURIComponent(name)}`;

  return {
    initialize: () => enqueue(async () => {
      if (connection) return;
      const hub = new signalR.HubConnectionBuilder()
        .withUrl(`${origin}/hubs/execution`).withAutomaticReconnect().build();
      const forward = (event: RuntimeEvent) => { if (connection === hub) emit(event); };
      hub.on('StateChanged', (data: { state: string; currentNode?: string }) => forward({ ...data, type: 'StateChanged' }));
      hub.on('NodeActivated', (nodeName: string) => forward({ type: 'NodeActivated', nodeName }));
      hub.on('NodeDeactivated', (nodeName: string) => forward({ type: 'NodeDeactivated', nodeName }));
      hub.on('EngineImported', () => forward({ type: 'EngineImported' }));
      hub.onreconnected(async () => {
        try { await hub.invoke('SubscribeToExecution'); }
        catch (error) { if (connection === hub) report(error); }
      });
      hub.onclose((error) => { if (connection === hub) report(error ?? new Error('Execution event connection closed. Retry initialization.')); });
      connection = hub;
      try {
        await hub.start();
        await hub.invoke('SubscribeToExecution');
      } catch (error) {
        connection = undefined;
        await hub.stop();
        throw error;
      }
    }),
    subscribe: (listener) => { listeners.add(listener); return () => { listeners.delete(listener); }; },
    dispose: () => enqueue(async () => {
      const hub = connection;
      connection = undefined;
      await hub?.stop();
    }),
    getStatus: () => request('/engine'),
    exportScript: () => request('/engine/export', 'GET', undefined, true),
    getModuleInstances: () => request('/engine/modules'),
    getModuleDefinitions: () => request('/engine/module-definitions'),
    createModuleInstance: (req) => request('/nodes/module-instance', 'POST', req),
    deleteModuleInstance: (instanceName) => request(modulePath(instanceName), 'DELETE'),
    updateModuleInstancePosition: (instanceName, posX, posY) => request(`${modulePath(instanceName)}/position`, 'PUT', { posX, posY }),
    importScript: (script) => request('/engine/import', 'POST', { script }),
    getNodes: () => request('/nodes'),
    getNode: (name) => request(namePath(name)),
    createNode: (req) => request('/nodes', 'POST', req),
    deleteNode: (name) => request(namePath(name), 'DELETE'),
    renameNode: (name, newName) => request(`${namePath(name)}/rename`, 'PUT', { newName }),
    updateNodePosition: (name, posX, posY) => request(`${namePath(name)}/position`, 'PUT', { posX, posY }),
    updateNodeEvent: (name, index, newEventName) => request(`${namePath(name)}/events/${index}`, 'PUT', { newEventName }),
    updateGroupDefs: (name, groupDefs) => request(`${namePath(name)}/groupdefs`, 'PUT', groupDefs),
    getConnections: () => request('/connections'),
    createConnection: (req) => request('/connections', 'POST', req),
    deleteConnection: (fromNodeName, toNodeName, eventName) => request('/connections', 'DELETE', { fromNodeName, toNodeName, eventName }),
    renameConnection: (fromNodeName, toNodeName, newEventName, eventName) => request('/connections/rename', 'PUT', { fromNodeName, toNodeName, eventName, newEventName }),
    getExecutionStatus: () => request('/execution/status'),
    startExecution: (startNodeName, endEventName) => request('/execution/start', 'POST', { startNodeName, endEventName }),
    pauseExecution: () => request('/execution/pause', 'POST'),
    continueExecution: () => request('/execution/continue', 'POST'),
    stopExecution: () => request('/execution/stop', 'POST'),
  };
}
