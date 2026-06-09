export interface EngineStatus {
  nodeNames: string[];
  eventNames: string[];
  connectionCount: number;
}

export interface NodeInfo {
  name: string;
  classType: string;
  color: string;
  posX: number;
  posY: number;
  flowID: string;
  discription?: string;
  eventDescriptions: NodeEventDescription[];
  groupDefs?: GroupDef[];
}

export interface NodeEventDescription {
  index: number;
  description: string;
}

export interface GroupDef {
  startNode: string;
  endEvent: string;
}

export interface ConnectionDto {
  fromNodeName: string;
  toNodeName: string;
  eventName: string;
}

export interface CreateNodeRequest {
  type: string;
  name: string;
  posX: number;
  posY: number;
  color: string;
}

export interface CreateConnectionRequest {
  fromNodeName: string;
  toNodeName: string;
  eventName: string;
}

export interface ExecutionStatus {
  state: string;
  currentNodeName?: string;
}

export interface ModuleInstance {
  instanceName: string;
  moduleName: string;
  posX: number;
  posY: number;
  color: string;
  flowID: string;
  internalNodeNames: string[];
  inputNodeNames: string[];
  terminalNodeNames: string[];
  outputEventMap: Record<string, string>;
  outputSourceNodes: Record<string, string>;
}

export interface ModuleDef {
  moduleName: string;
  inputs: string[];
  outputs: string[];
  terminals: string[];
}

export interface CreateModuleInstanceRequest {
  moduleName: string;
  instanceName: string;
  posX: number;
  posY: number;
}

export interface FlowNode {
  id: string;
  name: string;
  classType: string;
  color: string;
}

export interface FlowEdge {
  id: string;
  source: string;
  target: string;
  eventName: string;
}
