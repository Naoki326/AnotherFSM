const BASE_URL = 'http://localhost:5079/api';

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${url}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(err.error || res.statusText);
  }
  const text = await res.text();
  if (!text) return undefined as T;
  return JSON.parse(text);
}

export const api = {
  // Engine
  getStatus: () => request<import('./types').EngineStatus>('/engine'),
  exportScript: async () => {
    const res = await fetch(`${BASE_URL}/engine/export`);
    if (!res.ok) {
      const err = await res.json().catch(() => ({ error: res.statusText }));
      throw new Error(err.error || res.statusText);
    }
    return res.text();
  },
  getModuleInstances: () => request<import('./types').ModuleInstance[]>('/engine/modules'),
  getModuleDefinitions: () => request<import('./types').ModuleDef[]>('/engine/module-definitions'),
  createModuleInstance: (req: import('./types').CreateModuleInstanceRequest) =>
    request<import('./types').ModuleInstance>('/nodes/module-instance', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  deleteModuleInstance: (instanceName: string) =>
    request<void>(`/nodes/module-instance/${encodeURIComponent(instanceName)}`, { method: 'DELETE' }),
  updateModuleInstancePosition: (instanceName: string, posX: number, posY: number) =>
    request<void>(`/nodes/module-instance/${encodeURIComponent(instanceName)}/position`, {
      method: 'PUT',
      body: JSON.stringify({ posX, posY }),
    }),
  importScript: (script: string) =>
    request<import('./types').EngineStatus>('/engine/import', {
      method: 'POST',
      body: JSON.stringify({ script }),
    }),

  // Nodes
  getNodes: () => request<import('./types').NodeInfo[]>('/nodes'),
  getNode: (name: string) => request<import('./types').NodeInfo>(`/nodes/${encodeURIComponent(name)}`),
  createNode: (req: import('./types').CreateNodeRequest) =>
    request<import('./types').NodeInfo>('/nodes', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  deleteNode: (name: string) =>
    request<void>(`/nodes/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  renameNode: (name: string, newName: string) =>
    request<void>(`/nodes/${encodeURIComponent(name)}/rename`, {
      method: 'PUT',
      body: JSON.stringify({ newName }),
    }),
  updateNodePosition: (name: string, posX: number, posY: number) =>
    request<void>(`/nodes/${encodeURIComponent(name)}/position`, {
      method: 'PUT',
      body: JSON.stringify({ posX, posY }),
    }),
  updateNodeEvent: (name: string, index: number, newEventName: string) =>
    request<void>(`/nodes/${encodeURIComponent(name)}/events/${index}`, {
      method: 'PUT',
      body: JSON.stringify({ newEventName }),
    }),
  updateGroupDefs: (name: string, groupDefs: import('./types').GroupDef[]) =>
    request<void>(`/nodes/${encodeURIComponent(name)}/groupdefs`, {
      method: 'PUT',
      body: JSON.stringify(groupDefs),
    }),
  updateModuleTerminal: (name: string, isTerminal: boolean) =>
    request<void>(`/nodes/${encodeURIComponent(name)}/module-terminal`, {
      method: 'PUT',
      body: JSON.stringify({ isTerminal }),
    }),

  // Connections
  getConnections: () => request<import('./types').ConnectionDto[]>('/connections'),
  createConnection: (req: import('./types').CreateConnectionRequest) =>
    request<void>('/connections', {
      method: 'POST',
      body: JSON.stringify(req),
    }),
  deleteConnection: (fromNodeName: string, toNodeName: string, eventName?: string) =>
    request<void>('/connections', {
      method: 'DELETE',
      body: JSON.stringify({ fromNodeName, toNodeName, eventName }),
    }),
  renameConnection: (fromNodeName: string, toNodeName: string, newEventName: string, eventName?: string) =>
    request<void>('/connections/rename', {
      method: 'PUT',
      body: JSON.stringify({ fromNodeName, toNodeName, eventName, newEventName }),
    }),

  // Execution
  getExecutionStatus: () => request<import('./types').ExecutionStatus>('/execution/status'),
  startExecution: (startNodeName: string, endEventName: string) =>
    request<void>('/execution/start', {
      method: 'POST',
      body: JSON.stringify({ startNodeName, endEventName }),
    }),
  pauseExecution: () => request<void>('/execution/pause', { method: 'POST' }),
  continueExecution: () => request<void>('/execution/continue', { method: 'POST' }),
  stopExecution: () => request<void>('/execution/stop', { method: 'POST' }),
};
