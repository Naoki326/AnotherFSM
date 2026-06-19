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
import { message } from 'antd';

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

function edgeIdFor(source: string, target: string, eventName: string): string {
  return `${source}->${target}::${eventName}`;
}

function connectionEdgeId(c: ConnectionDto): string {
  return edgeIdFor(c.fromNodeName, c.toNodeName, c.eventName);
}

function toReactFlowEdge(c: ConnectionDto, isActive = false): Edge {
  return {
    id: connectionEdgeId(c),
    source: c.fromNodeName,
    target: c.toNodeName,
    label: c.eventName,
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
  const pendingConnections = useRef<Set<string>>(new Set());
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; nodeId: string } | null>(null);

  // Build set of module instance node names
  const moduleNodeNames = new Set(moduleInstances.map((m) => m.instanceName));

  // Map active node to its module instance if it's an internal node (e.g. CheckA.Entry -> CheckA)
  const activeModuleName = !viewModule && activeNodeName
    ? moduleInstances.find((m) => activeNodeName.startsWith(m.instanceName + '.'))?.instanceName ?? null
    : null;
  const effectiveActiveNodeName = activeModuleName ?? activeNodeName;

  const effectiveActiveTransition = useMemo<VisibleTransition | null>(() => {
    if (!activeTransition) return null;

    const resolveVisibleNodeName = (nodeName: string): string | null => {
      if (viewModule) {
        return nodeName.startsWith(viewModule + '.') ? nodeName : null;
      }
      const moduleInstance = moduleInstances.find((m) => nodeName.startsWith(m.instanceName + '.'));
      return moduleInstance?.instanceName ?? nodeName;
    };

    const source = resolveVisibleNodeName(activeTransition.fromNodeName);
    const target = resolveVisibleNodeName(activeTransition.toNodeName);
    if (!source || !target || source === target) return null;
    return { source, target };
  }, [activeTransition, moduleInstances, viewModule]);

  // Filter nodes/connections based on viewModule
  const filteredNodes = useMemo(() => {
    if (viewModule) {
      return storeNodes.filter((n) => n.name.startsWith(viewModule + '.'));
    }
    const regular = storeNodes.filter((n) => {
      for (const m of moduleInstances) {
        if (n.name.startsWith(m.instanceName + '.')) return false;
      }
      return true;
    });
    // Add module instances as virtual nodes
    const virtualNodes = moduleInstances.map((m) => ({
      name: m.instanceName,
      classType: m.moduleName,
      color: m.color,
      posX: m.posX,
      posY: m.posY,
      flowID: m.flowID,
      eventDescriptions: Object.entries(m.outputEventMap).map(([, extEvent], i) => ({
        index: i,
        description: extEvent,
      })),
    }));
    return [...regular, ...virtualNodes];
  }, [storeNodes, viewModule, moduleInstances]);

  const filteredConnections = useMemo(() => {
    if (viewModule) {
      return storeConnections.filter((c) =>
        c.fromNodeName.startsWith(viewModule + '.') && c.toNodeName.startsWith(viewModule + '.')
      );
    }
    // Root view: rewrite module-internal node names to module instance names, then deduplicate exact event edges.
    const instancePrefixes = moduleInstances.map((m) => ({ prefix: m.instanceName + '.', name: m.instanceName }));
    return storeConnections
      .map((c) => {
        let from = c.fromNodeName;
        let to = c.toNodeName;
        for (const { prefix, name } of instancePrefixes) {
          if (from.startsWith(prefix)) from = name;
          if (to.startsWith(prefix)) to = name;
        }
        return { ...c, fromNodeName: from, toNodeName: to };
      })
      .filter((c) => {
        // Hide self-connections within the same module instance (internal transitions)
        if (c.fromNodeName === c.toNodeName) return false;
        return true;
      })
      .filter((c, i, arr) => arr.findIndex((x) => connectionEdgeId(x) === connectionEdgeId(c)) === i);
  }, [storeConnections, viewModule, moduleInstances]);

  // Sync from filtered data — reads current active/selected via getState() to keep flags correct
  const viewKey = viewModule || '__root__';
  useEffect(() => {
    if (storeNodes.length === 0 && !initializedViewsRef.current.has(viewKey)) return;

    const { activeNodeName: curActive, selectedNodeName: curSelected } = useEngineStore.getState();
    let effActive = curActive;
    if (!viewModule && curActive) {
      const modInst = moduleInstances.find(m => curActive.startsWith(m.instanceName + '.'));
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
      const flags: Record<string, boolean> = {};
      if (innerStarts.has(n.name)) flags.isInnerStartNode = true;
      if (n.eventDescriptions.some(ed => endEventDescs.has(ed.description))) flags.isInnerEndNode = true;
      if (Object.keys(flags).length > 0) rn.data = { ...rn.data, ...flags };
      return rn;
    });

    const newEdges = filteredConnections.map((c) => toReactFlowEdge(c, isActiveConnection(c, effectiveActiveTransition)));
    if (!initializedViewsRef.current.has(viewKey)) {
      const allZero = newNodes.every(n => n.position.x === 0 && n.position.y === 0);
      setNodes(allZero ? autoLayout(newNodes, newEdges) : newNodes);
    } else {
      setNodes(newNodes);
    }
    setEdges(newEdges);
    initializedViewsRef.current.add(viewKey);
  }, [filteredNodes, filteredConnections, viewKey, effectiveActiveTransition]);

  // Sync active node highlight only (for changes without data sync)
  useEffect(() => {
    if (!initializedViewsRef.current.has(viewKey)) return;
    setNodes((nds) =>
      nds.map((n) => ({
        ...n,
        data: { ...n.data, isActive: n.id === effectiveActiveNodeName },
      }))
    );
  }, [effectiveActiveNodeName, viewKey]);

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
          const events = (n.data as any)?.events || [];
          if (events.some((ev: any) => endEventDescs.has(ev.description))) {
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
  }, [selectedNodeName, viewKey]);

  const onConnect = useCallback(
    async (connection: Connection) => {
      if (!connection.source || !connection.target) return;

      // Resolve module instance names to internal nodes for backend API
      const store = useEngineStore.getState();
      let fromName = connection.source;
      let toName = connection.target;
      let eventName = 'NextEvent';

      const sourceInst = store.moduleInstances.find(m => m.instanceName === connection.source);
      if (sourceInst) {
        fromName = sourceInst.instanceName;
        const firstEvent = Object.values(sourceInst.outputEventMap)[0];
        if (firstEvent) eventName = firstEvent;
      }

      const targetInst = store.moduleInstances.find(m => m.instanceName === connection.target);
      if (targetInst)
        toName = targetInst.instanceName;

      const edgeId = edgeIdFor(connection.source, connection.target, eventName);
      if (pendingConnections.current.has(edgeId)) return;
      pendingConnections.current.add(edgeId);

      const localEdge: Edge = {
        id: edgeId,
        source: connection.source,
        target: connection.target,
        sourceHandle: 'output',
        targetHandle: 'input',
        label: eventName,
        selectable: true,
        focusable: true,
        labelShowBg: true,
        markerEnd: { type: MarkerType.ArrowClosed },
        style: { stroke: '#555', strokeWidth: 1.5 },
        labelStyle: { fontSize: 10, fill: '#666' },
        labelBgStyle: { fill: '#fff', fillOpacity: 0.8 },
      };
      setEdges((eds) => {
        if (eds.some((e) => e.id === edgeId)) return eds;
        return [...eds, localEdge];
      });

      try {
        await api.createConnection({
          fromNodeName: fromName,
          toNodeName: toName,
          eventName,
        });
        const conns = await api.getConnections();
        useEngineStore.getState().setConnections(conns);
      } catch (e: any) {
        setEdges((eds) => eds.filter((e) => e.id !== edgeId));
        message.error(e.message || 'Failed to create connection');
      } finally {
        pendingConnections.current.delete(edgeId);
      }
    },
    [setEdges]
  );

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
          await api.deleteNode(node.id);
          useEngineStore.getState().removeNode(node.id);
        } catch {
          message.error(`Failed to delete node ${node.id}`);
        }
      }
    },
    []
  );

  const onEdgesDelete = useCallback(
    async (deleted: Edge[]) => {
      for (const edge of deleted) {
        try {
          await api.deleteConnection(edge.source, edge.target, String(edge.label || ''));
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
          const isInternalModuleNode = store.moduleInstances.some(m => node.id.startsWith(m.instanceName + '.'));
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
          const count = store.moduleInstances.filter((m) => m.moduleName === moduleType).length;
          const instanceName = `${moduleType}${count + 1}`;
          await api.createModuleInstance({
            moduleName: moduleType,
            instanceName,
            posX: Math.round(position.x),
            posY: Math.round(position.y),
          });
          const [nodes, mods, conns] = await Promise.all([api.getNodes(), api.getModuleInstances(), api.getConnections()]);
          store.setNodes(nodes);
          store.setModuleInstances(mods);
          store.setConnections(conns);
          try {
            const defs = await api.getModuleDefinitions();
            useEngineStore.getState().setModuleDefs(defs);
          } catch { /* ignore */ }
          message.success(`Created module instance: ${instanceName}`);
        } catch (e: any) {
          message.error(`Failed to create module instance: ${e.message}`);
        }
        return;
      }

      if (!nodeType) return;
      try {
        await api.createNode({
          type: nodeType, name: nodeType,
          posX: Math.round(position.x), posY: Math.round(position.y),
          color: nodeColor || '',
        });
        const nodes = await api.getNodes();
        useEngineStore.getState().setNodes(nodes);
        message.success(`${nodeType} node created`);
      } catch {
        message.error(`Failed to create ${nodeType} node`);
      }
    },
    [reactFlowInstance, setNodes]
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
        connectionMode={ConnectionMode.Loose}
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
            const c = (n.data as any)?.color || '';
            return c.startsWith('#') ? c : (NODE_COLORS[c] || '#999');
          }}
          nodeStrokeWidth={2}
        />
      </ReactFlow>
    </div>
  );
}
