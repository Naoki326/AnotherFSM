import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  type Node,
  type Edge,
  type Connection,
  type OnNodeDrag,
  useNodesState,
  useEdgesState,
  useReactFlow,
  BackgroundVariant,
  MarkerType,
  ConnectionMode,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import dagre from 'dagre';
import { useEngineStore } from '../../stores/engineStore';
import { api } from '../../api/client';
import type { NodeInfo, ConnectionDto } from '../../api/types';
import { FlowNode } from './FlowNode';
import { Button, message } from 'antd';
import { ConnectionDialog } from './ConnectionDialog';
import { connectionEdgeId, nextInstanceName, ownerOfNode, targetEndpoint, visibleConnections } from './moduleGraph';

const NODE_COLORS: Record<string, string> = {
  red: '#f44336', pink: '#e91e63', purple: '#9c27b0', 'deep-purple': '#673ab7',
  indigo: '#3f51b5', blue: '#2196f3', 'light-blue': '#03a9f4', cyan: '#00bcd4',
  teal: '#009688', green: '#4caf50', 'light-green': '#8bc34a', lime: '#cddc39',
  yellow: '#ffeb3b', amber: '#ffc107', orange: '#ff9800', 'deep-orange': '#ff5722',
  brown: '#795548', 'blue-grey': '#607d8b', grey: '#9e9e9e',
};

const nodeTypes = { flowNode: FlowNode };

function toReactFlowNode(n: NodeInfo, isActive: boolean, isSelected: boolean, isModuleInstance?: boolean): Node {
  const displayLabel = isModuleInstance ? `📦 ${n.name}` : n.name;
  return {
    id: n.name,
    type: 'flowNode',
    position: { x: n.posX, y: n.posY },
    data: {
      label: displayLabel,
      classType: n.classType,
      color: n.color,
      isActive,
      isSelected,
      events: n.eventDescriptions || [],
      groupDefs: n.groupDefs || [],
      isModuleInstance: !!isModuleInstance,
    },
  };
}

interface VisibleTransition {
  source: string;
  target: string;
}

function toReactFlowEdge(c: ConnectionDto, modules: import('../../api/types').ModuleInstance[], isActive = false): Edge {
  const target = targetEndpoint(c.toNodeName, modules);
  const sourceModule = modules.find((m) => m.instanceName === c.fromNodeName);
  return {
    id: connectionEdgeId(c),
    source: c.fromNodeName,
    target: target.node,
    sourceHandle: sourceModule ? `output:${c.eventName}` : 'output',
    targetHandle: target.handle ? `input:${target.handle}` : 'input',
    data: { connection: c },
    label: target.handle && Object.keys(modules.find((m) => m.instanceName === target.node)!.publicInputs).length > 1
      ? `${c.eventName} → ${target.handle}` : c.eventName,
    selectable: true,
    focusable: true,
    animated: isActive,
    className: isActive ? 'fsm-active-edge' : undefined,
    zIndex: isActive ? 10 : 0,
    markerEnd: { type: MarkerType.ArrowClosed, color: isActive ? '#1976d2' : '#555' },
    style: { stroke: isActive ? '#1976d2' : '#555', strokeWidth: isActive ? 3 : 1.5 },
    labelStyle: { fontSize: 10, fill: isActive ? '#1976d2' : '#666', fontWeight: isActive ? 700 : 400 },
    labelBgStyle: { fill: '#fff', fillOpacity: 0.8 },
    labelShowBg: true,
  };
}

function isActiveConnection(c: ConnectionDto, transition: VisibleTransition | null): boolean {
  return !!transition && c.fromNodeName === transition.source && c.toNodeName === transition.target;
}

function autoLayout(nodes: Node[], edges: Edge[]) {
  const g = new dagre.graphlib.Graph();
  g.setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir: 'LR', nodesep: 80, ranksep: 150 });
  for (const node of nodes) g.setNode(node.id, { width: 160, height: 60 });
  for (const edge of edges) g.setEdge(edge.source, edge.target);
  dagre.layout(g);
  return nodes.map((node) => {
    const pos = g.node(node.id);
    return { ...node, position: { x: pos.x - 80, y: pos.y - 30 } };
  });
}

interface FlowEditorProps {
  onNodeSelect?: (nodeName: string | null) => void;
}

export function FlowEditor({ onNodeSelect }: FlowEditorProps) {
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const activeNodeName = useEngineStore((s) => s.activeNodeName);
  const activeTransition = useEngineStore((s) => s.activeTransition);
  const selectedNodeName = useEngineStore((s) => s.selectedNodeName);
  const storeNodes = useEngineStore((s) => s.nodes);
  const storeConnections = useEngineStore((s) => s.connections);
  const moduleInstances = useEngineStore((s) => s.moduleInstances);
  const viewModule = useEngineStore((s) => s.viewModule);
  const initializedViewsRef = useRef<Set<string>>(new Set());
  const [newConnection, setNewConnection] = useState<Partial<ConnectionDto> | null>(null);
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; nodeId: string } | null>(null);

  // Build set of module instance node names
  const moduleNodeNames = useMemo(() => new Set(moduleInstances.map((m) => m.instanceName)), [moduleInstances]);

  // Map active node to its module instance if it's an internal node (e.g. CheckA.Entry -> CheckA)
  const activeModuleName = !viewModule && activeNodeName
    ? ownerOfNode(activeNodeName, moduleInstances)?.instanceName ?? null
    : null;
  const effectiveActiveNodeName = activeModuleName ?? activeNodeName;

  const effectiveActiveTransition = useMemo<VisibleTransition | null>(() => {
    if (!activeTransition) return null;

    const resolveVisibleNodeName = (nodeName: string): string | null => {
      const owner = ownerOfNode(nodeName, moduleInstances);
      if (viewModule) {
        return owner?.instanceName === viewModule ? nodeName : null;
      }
      return owner?.instanceName ?? nodeName;
    };

    const source = resolveVisibleNodeName(activeTransition.fromNodeName);
    const target = resolveVisibleNodeName(activeTransition.toNodeName);
    if (!source || !target || source === target) return null;
    return { source, target };
  }, [activeTransition, moduleInstances, viewModule]);

  // Filter nodes/connections based on viewModule
  const filteredNodes = useMemo(() => {
    if (viewModule) {
      return storeNodes.filter((n) => ownerOfNode(n.name, moduleInstances)?.instanceName === viewModule);
    }
    const regular = storeNodes.filter((n) => !ownerOfNode(n.name, moduleInstances));
    // Add module instances as virtual nodes
    const virtualNodes = moduleInstances.map((m) => ({
      name: m.instanceName,
      classType: m.moduleName,
      color: m.color,
      posX: m.posX,
      posY: m.posY,
      flowID: m.flowID,
      eventDescriptions: [],
    }));
    return [...regular, ...virtualNodes];
  }, [storeNodes, viewModule, moduleInstances]);

  const filteredConnections = useMemo(() => visibleConnections(storeConnections, moduleInstances, viewModule),
    [storeConnections, viewModule, moduleInstances]);

  // Sync from filtered data — reads current active/selected via getState() to keep flags correct
  const viewKey = viewModule || '__root__';
  useEffect(() => {
    if (storeNodes.length === 0 && !initializedViewsRef.current.has(viewKey)) return;

    const { activeNodeName: curActive, selectedNodeName: curSelected } = useEngineStore.getState();
    let effActive = curActive;
    if (!viewModule && curActive) {
      const modInst = ownerOfNode(curActive, moduleInstances);
      if (modInst) effActive = modInst.instanceName;
    }

    const innerStarts = new Set<string>();
    const endEventDescs = new Set<string>();
    if (curSelected) {
      const selNode = storeNodes.find(n => n.name === curSelected);
      if (selNode?.groupDefs) {
        for (const gd of selNode.groupDefs) {
          if (gd.startNode) innerStarts.add(gd.startNode);
          if (gd.endEvent) endEventDescs.add(gd.endEvent);
        }
      }
    }

    const newNodes = filteredNodes.map((n) => {
      const isMod = moduleNodeNames.has(n.name);
      const rn = toReactFlowNode(n, n.name === effActive, n.name === curSelected, isMod);
      const mod = moduleInstances.find((m) => m.instanceName === n.name);
      if (mod) rn.data = { ...rn.data, publicInputs: mod.publicInputs, publicOutputs: mod.publicOutputs };
      rn.deletable = !ownerOfNode(n.name, moduleInstances);
      const flags: Record<string, boolean> = {};
      if (innerStarts.has(n.name)) flags.isInnerStartNode = true;
      if (n.eventDescriptions.some(ed => endEventDescs.has(ed.description))) flags.isInnerEndNode = true;
      if (Object.keys(flags).length > 0) rn.data = { ...rn.data, ...flags };
      return rn;
    });

    const newEdges = filteredConnections.map((c) => toReactFlowEdge(c, viewModule ? [] : moduleInstances,
      isActiveConnection({ ...c, toNodeName: viewModule ? c.toNodeName : targetEndpoint(c.toNodeName, moduleInstances).node }, effectiveActiveTransition)));
    if (!initializedViewsRef.current.has(viewKey)) {
      const allZero = newNodes.every(n => n.position.x === 0 && n.position.y === 0);
      setNodes(allZero ? autoLayout(newNodes, newEdges) : newNodes);
    } else {
      setNodes(newNodes);
    }
    setEdges(newEdges);
    initializedViewsRef.current.add(viewKey);
  }, [filteredNodes, filteredConnections, viewKey, effectiveActiveTransition, storeNodes, viewModule, moduleInstances, moduleNodeNames, setNodes, setEdges]);

  // Sync active node highlight only (for changes without data sync)
  useEffect(() => {
    if (!initializedViewsRef.current.has(viewKey)) return;
    setNodes((nds) =>
      nds.map((n) => ({
        ...n,
        data: { ...n.data, isActive: n.id === effectiveActiveNodeName },
      }))
    );
  }, [effectiveActiveNodeName, viewKey, setNodes]);

  // Sync active transition highlight only
  useEffect(() => {
    if (!initializedViewsRef.current.has(viewKey)) return;
    setEdges((eds) =>
      eds.map((e) => {
        const isActive = !!effectiveActiveTransition
          && e.source === effectiveActiveTransition.source
          && e.target === effectiveActiveTransition.target;
        return {
          ...e,
          animated: isActive,
          className: isActive ? 'fsm-active-edge' : undefined,
          zIndex: isActive ? 10 : 0,
          markerEnd: { type: MarkerType.ArrowClosed, color: isActive ? '#1976d2' : '#555' },
          style: { stroke: isActive ? '#1976d2' : '#555', strokeWidth: isActive ? 3 : 1.5 },
          labelStyle: { ...(e.labelStyle || {}), fill: isActive ? '#1976d2' : '#666', fontWeight: isActive ? 700 : 400 },
        };
      })
    );
  }, [effectiveActiveTransition, setEdges, viewKey]);

  // Sync selection highlight only (for changes without data sync)
  useEffect(() => {
    if (!initializedViewsRef.current.has(viewKey)) return;
    const currentStoreNodes = useEngineStore.getState().nodes;
    const innerStarts = new Set<string>();
    const endEventDescs = new Set<string>();
    if (selectedNodeName) {
      const selNode = currentStoreNodes.find((n) => n.name === selectedNodeName);
      if (selNode?.groupDefs) {
        for (const gd of selNode.groupDefs) {
          if (gd.startNode) innerStarts.add(gd.startNode);
          if (gd.endEvent) endEventDescs.add(gd.endEvent);
        }
      }
    }
    setNodes((nds) => {
      const innerEnds = new Set<string>();
      if (endEventDescs.size > 0) {
        for (const n of nds) {
          const events = (n.data.events as NodeInfo['eventDescriptions'] | undefined) ?? [];
          if (events.some((ev) => endEventDescs.has(ev.description))) {
            innerEnds.add(n.id);
          }
        }
      }
      return nds.map((n) => ({
        ...n,
        data: {
          ...n.data,
          isSelected: n.id === selectedNodeName,
          isInnerStartNode: innerStarts.has(n.id),
          isInnerEndNode: innerEnds.has(n.id),
        },
      }));
    });
  }, [selectedNodeName, viewKey, setNodes]);

  const onConnect = useCallback((connection: Connection) => {
    if (!connection.source || !connection.target) return;
    const store = useEngineStore.getState();
    const target = store.moduleInstances.find((m) => m.instanceName === connection.target);
    const input = connection.targetHandle?.startsWith('input:') ? connection.targetHandle.slice(6) : undefined;
    const event = connection.sourceHandle?.startsWith('output:') ? connection.sourceHandle.slice(7)
      : store.nodes.find((n) => n.name === connection.source)?.eventDescriptions[0]?.description;
    setNewConnection({ fromNodeName: connection.source,
      toNodeName: target && Object.keys(target.publicInputs).length > 1
        ? (input ? `${target.instanceName}.${input}` : undefined) : connection.target,
      eventName: event });
  }, []);

  const onNodeClick = useCallback(
    (_: React.MouseEvent, node: Node) => {
      useEngineStore.getState().setSelectedNode(node.id);
      onNodeSelect?.(node.id);
      setContextMenu(null);
    },
    [onNodeSelect]
  );

  const onPaneClick = useCallback(() => {
    useEngineStore.getState().setSelectedNode(null);
    onNodeSelect?.(null);
    setContextMenu(null);
  }, [onNodeSelect]);

  const onEdgeClick = useCallback(
    (_: React.MouseEvent, edge: Edge) => {
      useEngineStore.getState().setSelectedEdge(edge.id);
      setContextMenu(null);
    },
    []
  );

  const onNodeContextMenu = useCallback(
    (event: React.MouseEvent, node: Node) => {
      event.preventDefault();
      if (moduleNodeNames.has(node.id)) {
        setContextMenu({ x: event.clientX, y: event.clientY, nodeId: node.id });
      }
    },
    [moduleNodeNames]
  );

  const onNodesDelete = useCallback(
    async (deleted: Node[]) => {
      for (const node of deleted) {
        try {
          const store = useEngineStore.getState();
          if (store.moduleInstances.some((m) => m.instanceName === node.id)) await api.deleteModuleInstance(node.id);
          else await api.deleteNode(node.id);
          await store.refreshGraph();
          store.setSelectedNode(null);
        } catch (e) {
          await useEngineStore.getState().refreshGraph();
          message.error(e instanceof Error ? e.message : `Failed to delete node ${node.id}`);
        }
      }
    },
    []
  );

  const onEdgesDelete = useCallback(
    async (deleted: Edge[]) => {
      for (const edge of deleted) {
        try {
          const c = edge.data?.connection as ConnectionDto;
          await api.deleteConnection(c.fromNodeName, c.toNodeName, c.eventName);
        } catch {
          setEdges((eds) => [...eds, edge]);
          message.error('Failed to delete connection');
        }
      }
      const conns = await api.getConnections();
      useEngineStore.getState().setConnections(conns);
    },
    [setEdges]
  );

  const onNodeDragStop = useCallback<OnNodeDrag>(
    async (_, node) => {
      try {
        const store = useEngineStore.getState();
        const isModule = store.moduleInstances.some(m => m.instanceName === node.id);
        if (isModule) {
          await api.updateModuleInstancePosition(node.id, node.position.x, node.position.y);
          const mods = store.moduleInstances;
          const idx = mods.findIndex(m => m.instanceName === node.id);
          if (idx >= 0) {
            mods[idx] = { ...mods[idx], posX: Math.round(node.position.x), posY: Math.round(node.position.y) };
            store.setModuleInstances([...mods]);
          }
        } else {
          await api.updateNodePosition(node.id, node.position.x, node.position.y);
          const isInternalModuleNode = !!ownerOfNode(node.id, store.moduleInstances);
          if (isInternalModuleNode) {
            const [nodes, mods, conns] = await Promise.all([api.getNodes(), api.getModuleInstances(), api.getConnections()]);
            store.setNodes(nodes);
            store.setModuleInstances(mods);
            store.setConnections(conns);
          } else {
            const nodes = store.nodes;
            const idx = nodes.findIndex(n => n.name === node.id);
            if (idx >= 0) {
              nodes[idx] = { ...nodes[idx], posX: Math.round(node.position.x), posY: Math.round(node.position.y) };
              store.setNodes([...nodes]);
            }
          }
        }
      } catch {
        message.error(`Failed to save position for node ${node.id}`);
      }
    },
    []
  );

  const reactFlowInstance = useReactFlow();
  const visibleNodeIds = filteredNodes.map((n) => n.name).join('\n');
  useEffect(() => {
    const timer = window.setTimeout(() => { void reactFlowInstance.fitView({ padding: 0.2 }); }, 100);
    return () => window.clearTimeout(timer);
  }, [visibleNodeIds, viewModule, reactFlowInstance]);

  const onDragOver = useCallback((event: React.DragEvent) => {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';
  }, []);

  const onDrop = useCallback(
    async (event: React.DragEvent) => {
      event.preventDefault();
      const position = reactFlowInstance.screenToFlowPosition({
        x: event.clientX,
        y: event.clientY,
      });

      const nodeType = event.dataTransfer.getData('application/reactflow-type');
      const nodeColor = event.dataTransfer.getData('application/reactflow-color');
      const moduleType = event.dataTransfer.getData('application/reactflow-module-type');

      if (moduleType) {
        try {
          const store = useEngineStore.getState();
          if (store.viewModule) throw new Error('Create Module Instances in Root, not inside a template.');
          const instanceName = nextInstanceName(moduleType, store.moduleInstances, store.nodes);
          await api.createModuleInstance({
            moduleName: moduleType,
            instanceName,
            posX: Math.round(position.x),
            posY: Math.round(position.y),
          });
          await store.refreshGraph();
          store.setSelectedNode(instanceName);
          message.success(`Created module instance: ${instanceName}`);
        } catch (e) {
          message.error(e instanceof Error ? e.message : 'Failed to create module instance');
        }
        return;
      }

      if (!nodeType) return;
      try {
        const store = useEngineStore.getState();
        if (store.viewModule) throw new Error('Add nodes to module declarations through Import. This canvas edits template positions, Branches and connections.');
        await api.createNode({
          type: nodeType, name: nodeType,
          posX: Math.round(position.x), posY: Math.round(position.y),
          color: nodeColor || '',
        });
        await store.refreshGraph();
        message.success(`${nodeType} node created`);
      } catch (e) {
        message.error(e instanceof Error ? e.message : `Failed to create ${nodeType} node`);
      }
    },
    [reactFlowInstance]
  );

  return (
    <div style={{ position: 'relative', width: '100%', height: '100%' }}>
      {/* Breadcrumb navigation */}
      <div
        style={{
          position: 'absolute',
          top: 8,
          left: 8,
          zIndex: 5,
          background: 'rgba(255,255,255,0.95)',
          borderRadius: 6,
          padding: '6px 12px',
          fontSize: 14,
          boxShadow: '0 2px 6px rgba(0,0,0,0.1)',
          display: 'flex',
          alignItems: 'center',
          gap: 8,
        }}
      >
        <Button size="small" onClick={() => setNewConnection({})}>Connect</Button>
        <span
          style={{ cursor: viewModule ? 'pointer' : 'default', color: viewModule ? '#1976d2' : '#333', fontWeight: 600 }}
          onClick={() => { if (viewModule) useEngineStore.getState().setViewModule(null); }}
        >
          Root
        </span>
        {viewModule && (
          <>
            <span style={{ color: '#999' }}>/</span>
            <span style={{ fontWeight: 600 }}>{viewModule}</span>
          </>
        )}
      </div>

      {/* Context menu */}
      {contextMenu && (
        <div
          style={{
            position: 'fixed',
            left: contextMenu.x,
            top: contextMenu.y,
            zIndex: 100,
            background: '#fff',
            borderRadius: 6,
            boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
            padding: '4px 0',
            minWidth: 160,
          }}
          onClick={() => setContextMenu(null)}
        >
          <div
            style={{ padding: '8px 16px', cursor: 'pointer', fontSize: 14 }}
            onClick={() => {
              useEngineStore.getState().setViewModule(contextMenu.nodeId);
              setContextMenu(null);
            }}
            onMouseEnter={(e) => (e.currentTarget.style.background = '#f5f5f5')}
            onMouseLeave={(e) => (e.currentTarget.style.background = 'transparent')}
          >
            📦 Enter Module
          </div>
        </div>
      )}

      {newConnection && <ConnectionDialog initial={newConnection} onClose={() => setNewConnection(null)} />}

      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        onNodeClick={onNodeClick}
        onEdgeClick={onEdgeClick}
        onPaneClick={onPaneClick}
        onNodesDelete={onNodesDelete}
        onEdgesDelete={onEdgesDelete}
        onNodeDragStop={onNodeDragStop}
        onDragOver={onDragOver}
        onDrop={onDrop}
        onNodeContextMenu={onNodeContextMenu}
        onContextMenu={(e) => e.preventDefault()}
        nodeTypes={nodeTypes}
        connectionMode={ConnectionMode.Strict}
        fitView
        deleteKeyCode={['Delete', 'Backspace']}
        edgesFocusable={true}
        defaultEdgeOptions={{ interactionWidth: 20 }}
        snapToGrid
        snapGrid={[15, 15]}
      >
        <Background variant={BackgroundVariant.Dots} gap={20} size={1} />
        <Controls />
        <MiniMap
          nodeColor={(n) => {
            const c = typeof n.data.color === 'string' ? n.data.color : '';
            return c.startsWith('#') ? c : (NODE_COLORS[c] || '#999');
          }}
          nodeStrokeWidth={2}
        />
      </ReactFlow>
    </div>
  );
}
