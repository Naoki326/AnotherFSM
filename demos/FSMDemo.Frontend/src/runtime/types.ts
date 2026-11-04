import type {
  EngineStatus, NodeInfo, ConnectionDto, ExecutionStatus, ModuleInstance, ModuleDef,
  CreateModuleInstanceRequest, CreateNodeRequest, CreateConnectionRequest, GroupDef,
} from '../api/types.ts';

export type RuntimeEvent =
  | { type: 'StateChanged'; state: string; currentNode?: string }
  | { type: 'NodeActivated' | 'NodeDeactivated'; nodeName: string }
  | { type: 'EngineImported' }
  | { type: 'RuntimeError'; error: string };

export type RuntimeListener = (event: RuntimeEvent) => void;

/** Transport-independent editing and execution contract. Subscribe before initialize. */
export interface EditorRuntime {
  initialize(): Promise<void>;
  subscribe(listener: RuntimeListener): () => void;
  dispose(): Promise<void>;
  getStatus(): Promise<EngineStatus>;
  exportScript(): Promise<string>;
  getModuleInstances(): Promise<ModuleInstance[]>;
  getModuleDefinitions(): Promise<ModuleDef[]>;
  createModuleInstance(req: CreateModuleInstanceRequest): Promise<ModuleInstance>;
  deleteModuleInstance(instanceName: string): Promise<void>;
  updateModuleInstancePosition(instanceName: string, posX: number, posY: number): Promise<void>;
  importScript(script: string): Promise<EngineStatus>;
  getNodes(): Promise<NodeInfo[]>;
  getNode(name: string): Promise<NodeInfo>;
  createNode(req: CreateNodeRequest): Promise<NodeInfo>;
  deleteNode(name: string): Promise<void>;
  renameNode(name: string, newName: string): Promise<void>;
  updateNodePosition(name: string, posX: number, posY: number): Promise<void>;
  updateNodeEvent(name: string, index: number, newEventName: string): Promise<void>;
  updateGroupDefs(name: string, groupDefs: GroupDef[]): Promise<void>;
  getConnections(): Promise<ConnectionDto[]>;
  createConnection(req: CreateConnectionRequest): Promise<void>;
  deleteConnection(fromNodeName: string, toNodeName: string, eventName?: string): Promise<void>;
  renameConnection(fromNodeName: string, toNodeName: string, newEventName: string, eventName?: string): Promise<void>;
  getExecutionStatus(): Promise<ExecutionStatus>;
  startExecution(startNodeName: string, endEventName: string): Promise<void>;
  pauseExecution(): Promise<void>;
  continueExecution(): Promise<void>;
  stopExecution(): Promise<void>;
}
