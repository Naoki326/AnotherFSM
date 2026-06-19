import { create } from 'zustand';
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

interface EngineState {
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
}

export const useEngineStore = create<EngineState>((set) => ({
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

  setStatus: (status) => set({ status }),
  setNodes: (nodes) => set({ nodes }),
  setConnections: (connections) => set({ connections }),
  setExecution: (execution) => set({ execution }),
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
