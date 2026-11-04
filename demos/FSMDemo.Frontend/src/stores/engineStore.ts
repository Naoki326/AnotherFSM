import { create } from 'zustand';
import { runtime } from '../runtime/index';
import type { RuntimeEvent } from '../runtime/types';
import type { EngineStatus, NodeInfo, ConnectionDto, ExecutionStatus, ModuleInstance, ModuleDef } from '../api/types';

interface ActiveTransition {
  fromNodeName: string;
  toNodeName: string;
}

interface ExecutionLogEntry {
  id: number;
  kind: 'enter' | 'exit' | 'state';
  message: string;
  nodeName?: string;
  createdAt: string;
}

let nextExecutionLogId = 1;
const EXECUTION_LOG_LIMIT = 80;
let sessionEpoch = 0;
let graphRevision = 0;
let executionRevision = 0;
let highlightRevision = 0;
let stopping = false;
let highlightTimer: ReturnType<typeof setTimeout> | undefined;
const clearHighlightTimer = () => {
  ++highlightRevision;
  clearTimeout(highlightTimer);
  highlightTimer = undefined;
};
const errorMessage = (error: unknown) => error instanceof Error ? error.message : String(error);

interface EngineState {
  runtimePhase: 'loading' | 'ready' | 'error';
  runtimeError: string | null;
  graphError: string | null;
  importing: boolean;
  initializeRuntime: () => Promise<void>;
  releaseRuntime: () => Promise<void>;
  handleRuntimeEvent: (event: RuntimeEvent) => void;
  resetExecution: () => void;
  stopExecution: () => Promise<void>;
  refreshExecution: () => Promise<void>;
  status: EngineStatus | null;
  nodes: NodeInfo[];
  connections: ConnectionDto[];
  execution: ExecutionStatus;
  selectedNodeName: string | null;
  selectedEdgeId: string | null;
  activeNodeName: string | null;
  activeTransition: ActiveTransition | null;
  executionLog: ExecutionLogEntry[];
  moduleInstances: ModuleInstance[];
  moduleDefs: ModuleDef[];
  viewModule: string | null;

  setStatus: (status: EngineStatus) => void;
  setNodes: (nodes: NodeInfo[]) => void;
  setConnections: (connections: ConnectionDto[]) => void;
  setExecution: (execution: ExecutionStatus) => void;
  setSelectedNode: (nodeName: string | null) => void;
  setSelectedEdge: (edgeId: string | null) => void;
  setActiveNode: (nodeName: string | null) => void;
  setActiveTransition: (transition: ActiveTransition | null) => void;
  appendExecutionLog: (entry: Omit<ExecutionLogEntry, 'id' | 'createdAt'>) => void;
  clearExecutionLog: () => void;
  setModuleDefs: (defs: ModuleDef[]) => void;
  setModuleInstances: (modules: ModuleInstance[]) => void;
  setViewModule: (moduleName: string | null) => void;
  removeNode: (name: string) => void;
  refreshGraph: (duringImport?: boolean) => Promise<void>;
  importScript: (script: string) => Promise<void>;
}

export const useEngineStore = create<EngineState>((set, get) => ({
  runtimePhase: 'loading',
  runtimeError: null,
  graphError: null,
  importing: false,
  initializeRuntime: async () => {
    const session = ++sessionEpoch;
    ++graphRevision;
    get().resetExecution();
    set({ runtimePhase: 'loading', runtimeError: null, graphError: null, importing: false });
    try {
      await runtime.initialize();
      if (session !== sessionEpoch) return;
      const revision = executionRevision;
      const [, execution] = await Promise.all([get().refreshGraph(), runtime.getExecutionStatus()]);
      if (session !== sessionEpoch) return;
      if (revision === executionRevision) set({ execution });
      set({ runtimePhase: 'ready' });
    } catch (error) {
      if (session === sessionEpoch) set({ runtimePhase: 'error', runtimeError: errorMessage(error) });
    }
  },
  releaseRuntime: async () => {
    const session = ++sessionEpoch;
    ++graphRevision;
    get().resetExecution();
    set({ runtimePhase: 'loading', importing: false });
    try { await runtime.dispose(); }
    catch (error) {
      if (session === sessionEpoch) set({ runtimePhase: 'error', runtimeError: errorMessage(error) });
    }
  },
  resetExecution: () => {
    ++executionRevision;
    clearHighlightTimer();
    set({ execution: { state: 'Idle' }, activeNodeName: null, activeTransition: null, executionLog: [] });
  },
  stopExecution: async () => {
    const session = sessionEpoch;
    stopping = true;
    const revision = ++executionRevision;
    clearHighlightTimer();
    set({ activeNodeName: null, activeTransition: null });
    try {
      await runtime.stopExecution();
      if (session === sessionEpoch && revision === executionRevision) {
        get().setExecution({ state: 'Idle' });
        get().appendExecutionLog({ kind: 'state', message: 'Stopped' });
      }
    } finally {
      stopping = false;
      if (session === sessionEpoch && (revision === executionRevision || get().execution.state === 'Idle')) {
        clearHighlightTimer();
        set({ activeNodeName: null, activeTransition: null });
      }
    }
  },
  refreshExecution: async () => {
    const session = sessionEpoch;
    const revision = executionRevision;
    const execution = await runtime.getExecutionStatus();
    if (session === sessionEpoch && revision === executionRevision && !get().importing) get().setExecution(execution);
  },
  handleRuntimeEvent: (event) => {
    if (event.type === 'RuntimeError') {
      set({ runtimeError: event.error });
      return;
    }
    if (get().runtimePhase !== 'ready') return;
    if (event.type === 'EngineImported') {
      get().resetExecution();
      set({ viewModule: null, selectedNodeName: null, selectedEdgeId: null });
      if (!get().importing) {
        const session = sessionEpoch;
        set({ importing: true });
        void get().refreshGraph(true).catch(() => { /* refreshGraph exposes graphError in UI. */ }).finally(() => {
          if (session === sessionEpoch) set({ importing: false });
        });
      }
      return;
    }
    if (get().importing || stopping) return;
    // The executor can announce its first node before its Running state.
    // Old executor notifications are isolated at the runtime boundary, not guessed from Idle.
    ++executionRevision;
    const store = get();
    if (event.type === 'StateChanged') {
      const previousState = store.execution.state;
      set({ execution: { state: event.state, currentNodeName: event.currentNode } });
      if (event.state === 'Finished') store.appendExecutionLog({ kind: 'state', message: 'Finished' });
      else if (event.state === 'Paused') store.appendExecutionLog({ kind: 'state', message: `Paused at ${event.currentNode || 'current node'}` });
      else if (event.state === 'Running' && previousState === 'Paused') store.appendExecutionLog({ kind: 'state', message: 'Continued' });
      else if (event.state === 'Idle' && previousState !== 'Idle' && previousState !== 'Finished') store.appendExecutionLog({ kind: 'state', message: 'Stopped' });
      if (event.state === 'Idle') {
        clearHighlightTimer();
        set({ activeNodeName: null, activeTransition: null });
      }
    } else if (event.type === 'NodeActivated') {
      clearHighlightTimer();
      set({ activeNodeName: event.nodeName, activeTransition: store.activeNodeName && store.activeNodeName !== event.nodeName
        ? { fromNodeName: store.activeNodeName, toNodeName: event.nodeName } : null });
      store.appendExecutionLog({ kind: 'enter', message: `Enter ${event.nodeName}`, nodeName: event.nodeName });
    } else if (event.type === 'NodeDeactivated') {
      store.appendExecutionLog({ kind: 'exit', message: `Exit ${event.nodeName}`, nodeName: event.nodeName });
      clearHighlightTimer();
      const revision = highlightRevision;
      highlightTimer = setTimeout(() => {
        if (revision === highlightRevision && get().activeNodeName === event.nodeName) {
          set({ activeNodeName: null, activeTransition: null });
        }
        highlightTimer = undefined;
      }, 600);
    }
  },
  status: null,
  nodes: [],
  connections: [],
  execution: { state: 'Idle' },
  selectedNodeName: null,
  selectedEdgeId: null,
  activeNodeName: null,
  activeTransition: null,
  executionLog: [],
  moduleInstances: [],
  moduleDefs: [],
  viewModule: null,

  refreshGraph: async (duringImport = false) => {
    // Late completions from edits must not read or replace the graph mid-import.
    if (get().importing && !duringImport) return;
    const session = sessionEpoch;
    const revision = ++graphRevision;
    try {
      const [status, nodes, connections, moduleInstances, moduleDefs] = await Promise.all([
        runtime.getStatus(), runtime.getNodes(), runtime.getConnections(), runtime.getModuleInstances(), runtime.getModuleDefinitions(),
      ]);
      if (session === sessionEpoch && revision === graphRevision) {
        set({ status, nodes, connections, moduleInstances, moduleDefs, graphError: null });
      }
    } catch (error) {
      if (session === sessionEpoch && revision === graphRevision) set({ graphError: errorMessage(error) });
      throw error;
    }
  },
  importScript: async (script) => {
    if (get().importing) throw new Error('An import is already in progress.');
    const session = sessionEpoch;
    ++graphRevision; // Reject graph reads that started before this import.
    set({ importing: true });
    try {
      await runtime.importScript(script);
      if (session !== sessionEpoch) return;
      get().resetExecution();
      set({ viewModule: null, selectedNodeName: null, selectedEdgeId: null });
      await get().refreshGraph(true);
    } catch (error) {
      // Failed validation keeps the old executor alive. Recover any state change
      // whose notification was suppressed while the import was pending.
      if (session === sessionEpoch) {
        try {
          const execution = await runtime.getExecutionStatus();
          if (session === sessionEpoch) get().setExecution(execution);
        } catch (statusError) {
          if (session === sessionEpoch) set({ runtimeError: errorMessage(statusError) });
        }
      }
      throw error;
    } finally {
      if (session === sessionEpoch) set({ importing: false });
    }
  },
  setStatus: (status) => set({ status }),
  setNodes: (nodes) => set({ nodes }),
  setConnections: (connections) => set({ connections }),
  setExecution: (execution) => {
    ++executionRevision;
    if (execution.state === 'Idle') {
      clearHighlightTimer();
      set({ execution, activeNodeName: null, activeTransition: null });
    } else set({ execution });
  },
  setSelectedNode: (selectedNodeName) => set({ selectedNodeName, selectedEdgeId: null }),
  setSelectedEdge: (selectedEdgeId) => set({ selectedEdgeId, selectedNodeName: null }),
  setActiveNode: (activeNodeName) => set({ activeNodeName }),
  setActiveTransition: (activeTransition) => set({ activeTransition }),
  appendExecutionLog: (entry) =>
    set((state) => ({
      executionLog: [
        ...state.executionLog,
        {
          ...entry,
          id: nextExecutionLogId++,
          createdAt: new Date().toLocaleTimeString(undefined, { hour12: false }),
        },
      ].slice(-EXECUTION_LOG_LIMIT),
    })),
  clearExecutionLog: () => set({ executionLog: [] }),
  setModuleDefs: (moduleDefs) => set({ moduleDefs }),
  setModuleInstances: (moduleInstances) => set({ moduleInstances }),
  setViewModule: (viewModule) => set({ viewModule, selectedNodeName: null, selectedEdgeId: null }),
  removeNode: (name) =>
    set((state) => ({
      nodes: state.nodes.filter((n) => n.name !== name),
      connections: state.connections.filter(
        (c) => c.fromNodeName !== name && c.toNodeName !== name
      ),
    })),
}));
