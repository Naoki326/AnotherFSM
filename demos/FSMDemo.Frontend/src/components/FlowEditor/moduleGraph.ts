import type { ConnectionDto, ModuleInstance, NodeInfo } from '../../api/types.ts';

export function connectionEdgeId(c: ConnectionDto): string {
  return JSON.stringify([c.fromNodeName, c.toNodeName, c.eventName]);
}

export function ownerOfNode(name: string, modules: ModuleInstance[]) {
  return modules.find((m) => m.internalNodeNames.includes(name));
}

export function targetEndpoint(name: string, modules: ModuleInstance[]) {
  for (const m of modules) {
    const inputs = Object.keys(m.publicInputs);
    if (name === m.instanceName) return { node: m.instanceName, handle: inputs.length === 1 ? inputs[0] : undefined };
    for (const input of Object.keys(m.publicInputs)) {
      if (name === `${m.instanceName}.${input}`) return { node: m.instanceName, handle: input };
    }
  }
  return { node: name, handle: undefined };
}

export function visibleConnections(connections: ConnectionDto[], modules: ModuleInstance[], view: string | null) {
  const seen = new Set<string>();
  return connections.filter((c) => {
    const fromOwner = ownerOfNode(c.fromNodeName, modules);
    const toOwner = ownerOfNode(c.toNodeName, modules);
    if (view) {
      if (fromOwner?.instanceName !== view || toOwner?.instanceName !== view) return false;
    } else if (fromOwner || (toOwner && targetEndpoint(c.toNodeName, modules).node === c.toNodeName)) {
      // The API supplies canonical public interface edges. Never collapse arbitrary private nodes into interfaces.
      return false;
    }
    const id = connectionEdgeId(c);
    if (seen.has(id)) return false;
    seen.add(id);
    return true;
  });
}

export function connectionTargets(nodes: NodeInfo[], modules: ModuleInstance[], view: string | null) {
  const regular = nodes.filter((n) => view
    ? ownerOfNode(n.name, modules)?.instanceName === view
    : !ownerOfNode(n.name, modules)).map((n) => ({ value: n.name, label: n.name }));
  if (view) return regular;
  return [...regular, ...modules.flatMap((m) => {
    const inputs = Object.keys(m.publicInputs);
    return inputs.map((input) => ({
      value: inputs.length === 1 ? m.instanceName : `${m.instanceName}.${input}`,
      label: inputs.length === 1 ? `${m.instanceName} (Input: ${input})` : `${m.instanceName}.${input}`,
    }));
  })];
}

export function validateConnection(c: ConnectionDto, nodes: NodeInfo[], modules: ModuleInstance[], view: string | null) {
  const sourceModule = modules.find((m) => m.instanceName === c.fromNodeName);
  if (sourceModule) {
    if (view || !Object.values(sourceModule.publicOutputs).includes(c.eventName)) throw new Error('Select a public Output of the source instance.');
  } else {
    const source = nodes.find((n) => n.name === c.fromNodeName);
    if (!source || (ownerOfNode(source.name, modules)?.instanceName ?? null) !== view) throw new Error('Source is not accessible in this canvas.');
    if (!c.eventName.trim()) throw new Error('Select or enter an event.');
  }
  if (!connectionTargets(nodes, modules, view).some((t) => t.value === c.toNodeName)) {
    throw new Error('Select a public Input; multi-input instances require an explicit Input.');
  }
  return c;
}

export function nextInstanceName(moduleName: string, modules: ModuleInstance[], nodes: NodeInfo[]) {
  let index = 1;
  const names = new Set([...modules.map((m) => m.instanceName), ...nodes.map((n) => n.name)]);
  while ([...names].some((n) => n === `${moduleName}${index}` || n.startsWith(`${moduleName}${index}.`))) index++;
  return `${moduleName}${index}`;
}
