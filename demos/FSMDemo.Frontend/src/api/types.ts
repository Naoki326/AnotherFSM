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
}

export interface NodeEventDescription {
  index: number;
  description: string;
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
  publicInputs: Record<string, string>;
  publicOutputs: Record<string, string>;
  outputPublishers: Record<string, string[]>;
}

export interface ModuleDef {
  moduleName: string;
  inputs: string[];
  outputs: string[];
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
