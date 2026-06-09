import { useCallback, useEffect, useState } from 'react';
import { Card, Descriptions, Tag, Button, Space, Popconfirm, Input, message, Checkbox } from 'antd';
import { PlusOutlined, MinusCircleOutlined } from '@ant-design/icons';
import { useEngineStore } from '../../stores/engineStore';
import { api } from '../../api/client';
import type { NodeInfo, ConnectionDto, GroupDef } from '../../api/types';

function edgeIdFor(source: string, target: string, eventName: string): string {
  return `${source}->${target}::${eventName}`;
}

export function PropertyPanel() {
  const selectedNodeName = useEngineStore((s) => s.selectedNodeName);
  const selectedEdgeId = useEngineStore((s) => s.selectedEdgeId);
  const nodes = useEngineStore((s) => s.nodes);
  const connections = useEngineStore((s) => s.connections);
  const moduleInstances = useEngineStore((s) => s.moduleInstances);
  const [node, setNode] = useState<NodeInfo | null>(null);

  const selectedConnection = selectedEdgeId
    ? (() => {
        const direct = connections.find((c) => edgeIdFor(c.fromNodeName, c.toNodeName, c.eventName) === selectedEdgeId);
        if (direct) return direct;
        const { moduleInstances: mods, viewModule } = useEngineStore.getState();
        if (!viewModule) {
          return connections.find((c) => {
            let from = c.fromNodeName, to = c.toNodeName;
            for (const m of mods) {
              const prefix = m.instanceName + '.';
              if (from.startsWith(prefix)) from = m.instanceName;
              if (to.startsWith(prefix)) to = m.instanceName;
            }
            return edgeIdFor(from, to, c.eventName) === selectedEdgeId;
          }) || null;
        }
        return null;
      })()
    : null;

  const selectedModule = selectedNodeName
    ? moduleInstances.find((m) => m.instanceName === selectedNodeName) || null
    : null;
  const moduleDefs = useEngineStore((s) => s.moduleDefs);
  const selectedModuleDef = selectedModule
    ? moduleDefs.find((d) => d.moduleName === selectedModule.moduleName) || null
    : null;
  const selectedModuleNode = selectedNodeName
    ? moduleInstances.find((m) => selectedNodeName.startsWith(m.instanceName + '.')) || null
    : null;
  const selectedModuleLocalNodeName = selectedModuleNode && selectedNodeName
    ? selectedNodeName.slice(selectedModuleNode.instanceName.length + 1)
    : null;
  const selectedModuleNodeIsTerminal = !!selectedModuleNode
    && !!selectedNodeName
    && selectedModuleNode.terminalNodeNames.some((n) => n === selectedNodeName);

  useEffect(() => {
    if (selectedNodeName) {
      const found = nodes.find((n) => n.name === selectedNodeName) || null;
      setNode(found);
    } else {
      setNode(null);
    }
  }, [selectedNodeName, nodes]);

  const nodeConnections = connections.filter(
    (c) => c.fromNodeName === selectedNodeName || c.toNodeName === selectedNodeName
  );

  const refreshGraph = useCallback(async () => {
    const [nodes, mods, conns, defs] = await Promise.all([
      api.getNodes(),
      api.getModuleInstances(),
      api.getConnections(),
      api.getModuleDefinitions(),
    ]);
    const store = useEngineStore.getState();
    store.setNodes(nodes);
    store.setModuleInstances(mods);
    store.setConnections(conns);
    store.setModuleDefs(defs);
  }, []);

  const handleRename = useCallback(async () => {
    if (!node) return;
    try {
      const newName = prompt('New name:', node.name);
      if (newName && newName !== node.name) {
        await api.renameNode(node.name, newName);
        const [ns, conns] = await Promise.all([api.getNodes(), api.getConnections()]);
        useEngineStore.getState().setNodes(ns);
        useEngineStore.getState().setConnections(conns);
        useEngineStore.getState().setSelectedNode(newName);
      }
    } catch {
      message.error('Rename failed');
    }
  }, [node]);

  const handleDeleteNode = useCallback(async () => {
    if (!node) return;
    try {
      await api.deleteNode(node.name);
      useEngineStore.getState().removeNode(node.name);
      useEngineStore.getState().setSelectedNode(null);
      message.success(`Deleted node: ${node.name}`);
    } catch {
      message.error('Delete failed');
    }
  }, [node]);

  const handleRenameConnection = useCallback(async (conn: ConnectionDto) => {
    try {
      const newName = prompt('New event name:', conn.eventName);
      if (newName && newName !== conn.eventName) {
        await api.renameConnection(conn.fromNodeName, conn.toNodeName, newName, conn.eventName);
        const conns = await api.getConnections();
        useEngineStore.getState().setConnections(conns);
        message.success('Connection renamed');
      }
    } catch {
      message.error('Rename connection failed');
    }
  }, []);

  const handleDeleteConnection = useCallback(async (conn: ConnectionDto) => {
    try {
      await api.deleteConnection(conn.fromNodeName, conn.toNodeName, conn.eventName);
      const conns = await api.getConnections();
      useEngineStore.getState().setConnections(conns);
      useEngineStore.getState().setSelectedEdge(null);
      message.success('Connection deleted');
    } catch {
      message.error('Delete connection failed');
    }
  }, []);

  const handleGroupDefChange = useCallback(async (nodeName: string, newDefs: GroupDef[]) => {
    try {
      await api.updateGroupDefs(nodeName, newDefs);
      await refreshGraph();
    } catch {
      message.error('Failed to update group definitions');
    }
  }, [refreshGraph]);

  const handleModuleTerminalChange = useCallback(async (nodeName: string, isTerminal: boolean) => {
    try {
      await api.updateModuleTerminal(nodeName, isTerminal);
      await refreshGraph();
      message.success(isTerminal ? 'Terminal set' : 'Terminal removed');
    } catch {
      message.error('Failed to update terminal');
    }
  }, [refreshGraph]);

  // Edge selected
  if (!node && selectedConnection) {
    return (
      <Card title="Connection Properties" style={{ height: '100%', overflow: 'auto', fontSize: 22 }}>
        <Descriptions column={1} bordered labelStyle={{ fontSize: 20 }} contentStyle={{ fontSize: 20 }}>
          <Descriptions.Item label="From">{selectedConnection.fromNodeName}</Descriptions.Item>
          <Descriptions.Item label="To">{selectedConnection.toNodeName}</Descriptions.Item>
          <Descriptions.Item label="Event">
            <Tag style={{ fontSize: 18 }}>{selectedConnection.eventName}</Tag>
          </Descriptions.Item>
        </Descriptions>
        <Space style={{ marginTop: 16 }} size="middle">
          <Button onClick={() => handleRenameConnection(selectedConnection)}>Rename Event</Button>
          <Popconfirm title="Delete this connection?" onConfirm={() => handleDeleteConnection(selectedConnection)} okText="Delete" cancelText="Cancel">
            <Button danger>Delete Connection</Button>
          </Popconfirm>
        </Space>
      </Card>
    );
  }

  // Module instance selected
  if (!node && selectedModule) {
    const handleDeleteModule = async () => {
      try {
        await api.deleteModuleInstance(selectedModule.instanceName);
        const [nodes, mods, conns] = await Promise.all([api.getNodes(), api.getModuleInstances(), api.getConnections()]);
        useEngineStore.getState().setNodes(nodes);
        useEngineStore.getState().setModuleInstances(mods);
        useEngineStore.getState().setConnections(conns);
        useEngineStore.getState().setSelectedNode(null);
        message.success(`Deleted module instance: ${selectedModule.instanceName}`);
      } catch {
        message.error('Failed to delete module instance');
      }
    };

    return (
      <Card title="Module Instance" style={{ height: '100%', overflow: 'auto', fontSize: 22 }}>
        <Descriptions column={1} bordered labelStyle={{ fontSize: 20 }} contentStyle={{ fontSize: 20 }}>
          <Descriptions.Item label="Instance">{selectedModule.instanceName}</Descriptions.Item>
          <Descriptions.Item label="Module">
            <Tag style={{ fontSize: 18 }} color="purple">{selectedModule.moduleName}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Position">
            ({selectedModule.posX.toFixed(0)}, {selectedModule.posY.toFixed(0)})
          </Descriptions.Item>
        </Descriptions>

        {selectedModuleDef && (
          <>
            <div style={{ marginTop: 16 }}>
              <strong style={{ fontSize: 20 }}>Inputs:</strong>
              {selectedModuleDef.inputs.length > 0
                ? selectedModuleDef.inputs.map((inp) => (
                    <Tag key={inp} style={{ fontSize: 16, marginLeft: 8 }} color="blue">{inp}</Tag>
                  ))
                : <span style={{ fontSize: 16, color: '#999' }}> none</span>}
            </div>
            <div style={{ marginTop: 8 }}>
              <strong style={{ fontSize: 20 }}>Outputs:</strong>
              {selectedModuleDef.outputs.length > 0
                ? selectedModuleDef.outputs.map((out) => (
                    <Tag key={out} style={{ fontSize: 16, marginLeft: 8 }} color="green">{out}</Tag>
                  ))
                : <span style={{ fontSize: 16, color: '#999' }}> none</span>}
            </div>
            <div style={{ marginTop: 8 }}>
              <strong style={{ fontSize: 20 }}>Terminals:</strong>
              {selectedModuleDef.terminals.length > 0
                ? selectedModuleDef.terminals.map((terminal) => (
                    <Tag key={terminal} style={{ fontSize: 16, marginLeft: 8 }} color="purple">{terminal}</Tag>
                  ))
                : <span style={{ fontSize: 16, color: '#999' }}> none</span>}
            </div>
          </>
        )}

        <div style={{ marginTop: 16 }}>
          <strong style={{ fontSize: 20 }}>Output Event Mapping:</strong>
          {Object.entries(selectedModule.outputEventMap).map(([key, val]) => (
            <div key={key} style={{ marginTop: 8, fontSize: 18, display: 'flex', alignItems: 'center', gap: 8 }}>
              <Tag style={{ fontSize: 16 }} color="blue">[{key}]</Tag>
              <span>→</span>
              <Tag style={{ fontSize: 16 }} color="green">{val}</Tag>
            </div>
          ))}
        </div>

        {selectedModule.internalNodeNames.length > 0 && (
          <div style={{ marginTop: 16 }}>
            <strong style={{ fontSize: 20 }}>Internal Nodes:</strong>
            {selectedModule.internalNodeNames.map((n) => (
              <div key={n} style={{ fontSize: 16, marginTop: 4, color: '#666' }}>{n}</div>
            ))}
          </div>
        )}

        {selectedModule.terminalNodeNames.length > 0 && (
          <div style={{ marginTop: 16 }}>
            <strong style={{ fontSize: 20 }}>Terminal Nodes:</strong>
            {selectedModule.terminalNodeNames.map((n) => (
              <div key={n} style={{ fontSize: 16, marginTop: 4, color: '#666' }}>{n}</div>
            ))}
          </div>
        )}

        <Space style={{ marginTop: 16 }} size="middle">
          <Button
            type="primary"
            onClick={() => useEngineStore.getState().setViewModule(selectedModule.instanceName)}
          >
            Enter Module
          </Button>
          <Popconfirm title="Delete this module instance?" onConfirm={handleDeleteModule} okText="Delete" cancelText="Cancel">
            <Button danger>Delete Module</Button>
          </Popconfirm>
        </Space>
      </Card>
    );
  }

  // Nothing selected
  if (!node) {
    return (
      <Card title="Properties" style={{ height: '100%', fontSize: 22 }}>
        <span style={{ color: '#999' }}>Select a node or connection to view properties</span>
      </Card>
    );
  }

  const isGroupType = node.classType?.startsWith('Group');
  const isParallelType = node.classType?.startsWith('Parallel');
  const isGroupNode = isGroupType || isParallelType;

  // Node selected
  return (
    <Card title="Node Properties" style={{ height: '100%', overflow: 'auto', fontSize: 22 }}>
      <Descriptions column={1} bordered labelStyle={{ fontSize: 20 }} contentStyle={{ fontSize: 20 }}>
        <Descriptions.Item label="Name">{node.name}</Descriptions.Item>
        <Descriptions.Item label="Type">
          <Tag style={{ fontSize: 18 }} color="blue">{node.classType}</Tag>
        </Descriptions.Item>
        <Descriptions.Item label="Color">
          <Tag style={{ fontSize: 18 }} color={node.color}>{node.color}</Tag>
        </Descriptions.Item>
        <Descriptions.Item label="Position">
          ({node.posX.toFixed(0)}, {node.posY.toFixed(0)})
        </Descriptions.Item>
      </Descriptions>

      <Space style={{ marginTop: 16 }} size="middle">
        <Button onClick={handleRename}>Rename Node</Button>
        <Popconfirm title="Delete this node?" onConfirm={handleDeleteNode} okText="Delete" cancelText="Cancel">
          <Button danger>Delete Node</Button>
        </Popconfirm>
      </Space>

      {selectedModuleNode && selectedModuleLocalNodeName && (
        <div style={{ marginTop: 16 }}>
          <strong style={{ fontSize: 20 }}>Module Role:</strong>
          <div style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <Tag style={{ fontSize: 16 }} color="purple">{selectedModuleNode.moduleName}</Tag>
            <Tag style={{ fontSize: 16 }}>{selectedModuleLocalNodeName}</Tag>
            <Checkbox
              checked={selectedModuleNodeIsTerminal}
              onChange={(e) => handleModuleTerminalChange(node.name, e.target.checked)}
              style={{ fontSize: 18 }}
            >
              Terminal
            </Checkbox>
          </div>
        </div>
      )}

      {isGroupNode && (
        <div style={{ marginTop: 16 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
            <strong style={{ fontSize: 20 }}>
              {isParallelType ? 'Inner FSMs:' : 'Inner FSM:'}
            </strong>
            {isParallelType && (
              <Button
                icon={<PlusOutlined />}
                size="small"
                onClick={() => {
                  const newDefs = [...(node.groupDefs || []), { startNode: '', endEvent: '' }];
                  handleGroupDefChange(node.name, newDefs);
                }}
              >
                Add FSM
              </Button>
            )}
          </div>
          {(node.groupDefs || []).map((def, i) => (
            <div key={i} style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
              <Input
                addonBefore="Start"
                key={`${node.name}-gd-${i}-start`}
                defaultValue={def.startNode}
                size="large"
                style={{ fontSize: 16, flex: 1, minWidth: 100 }}
                onBlur={(e) => {
                  if (e.target.value !== def.startNode) {
                    const newDefs = [...(node.groupDefs || [])];
                    newDefs[i] = { ...newDefs[i], startNode: e.target.value };
                    handleGroupDefChange(node.name, newDefs);
                  }
                }}
                onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
              />
              <span style={{ fontSize: 20 }}>→</span>
              <Input
                addonBefore="End"
                key={`${node.name}-gd-${i}-end`}
                defaultValue={def.endEvent}
                size="large"
                style={{ fontSize: 16, flex: 1, minWidth: 100 }}
                onBlur={(e) => {
                  if (e.target.value !== def.endEvent) {
                    const newDefs = [...(node.groupDefs || [])];
                    newDefs[i] = { ...newDefs[i], endEvent: e.target.value };
                    handleGroupDefChange(node.name, newDefs);
                  }
                }}
                onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
              />
              {isParallelType && (node.groupDefs || []).length > 1 && (
                <Button
                  icon={<MinusCircleOutlined />}
                  danger
                  size="large"
                  onClick={() => {
                    const newDefs = (node.groupDefs || []).filter((_, idx) => idx !== i);
                    handleGroupDefChange(node.name, newDefs);
                  }}
                />
              )}
            </div>
          ))}
        </div>
      )}

      {node.eventDescriptions.length > 0 && (
        <div style={{ marginTop: 16 }}>
          <strong style={{ fontSize: 20 }}>Events:</strong>
          {node.eventDescriptions.map((ed) => (
            <div key={ed.index} style={{ marginTop: 8, fontSize: 20, display: 'flex', alignItems: 'center', gap: 8 }}>
              <Tag style={{ fontSize: 18 }}>{ed.index}</Tag>
              <span>→</span>
              <Input
                key={`${node.name}-ev-${ed.index}`}
                defaultValue={ed.description}
                size="large"
                style={{ fontSize: 18, flex: 1 }}
                onBlur={async (e) => {
                  if (e.target.value !== ed.description) {
                    try {
                      await api.updateNodeEvent(node.name, ed.index, e.target.value);
                      await refreshGraph();
                    } catch {
                      message.error('Failed to update event');
                    }
                  }
                }}
                onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
              />
            </div>
          ))}
        </div>
      )}

      {nodeConnections.length > 0 && (
        <div style={{ marginTop: 16 }}>
          <strong style={{ fontSize: 20 }}>Connections:</strong>
          {nodeConnections.map((c) => (
            <div key={`${c.fromNodeName}-${c.toNodeName}-${c.eventName}`} style={{ fontSize: 20, marginTop: 6 }}>
              {c.fromNodeName === node.name ? (
                <span>→ <strong>{c.toNodeName}</strong> ({c.eventName})</span>
              ) : (
                <span><strong>{c.fromNodeName}</strong> → ({c.eventName})</span>
              )}
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}
