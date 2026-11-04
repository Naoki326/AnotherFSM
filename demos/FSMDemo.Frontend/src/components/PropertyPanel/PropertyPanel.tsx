import { useCallback } from 'react';
import { Card, Descriptions, Tag, Button, Space, Popconfirm, Input, message } from 'antd';
import { PlusOutlined, MinusCircleOutlined } from '@ant-design/icons';
import { useEngineStore } from '../../stores/engineStore';
import { api } from '../../api/client';
import type { ConnectionDto, GroupDef } from '../../api/types';

import { connectionEdgeId, ownerOfNode } from '../FlowEditor/moduleGraph';

const descLabelStyle = { fontSize: 14, whiteSpace: 'nowrap' as const };
const descContentStyle = { fontSize: 14, wordBreak: 'break-all' as const };

export function PropertyPanel() {
  const selectedNodeName = useEngineStore((s) => s.selectedNodeName);
  const selectedEdgeId = useEngineStore((s) => s.selectedEdgeId);
  const nodes = useEngineStore((s) => s.nodes);
  const connections = useEngineStore((s) => s.connections);
  const moduleInstances = useEngineStore((s) => s.moduleInstances);
  const node = nodes.find((n) => n.name === selectedNodeName) ?? null;

  const selectedConnection = selectedEdgeId ? connections.find((c) => connectionEdgeId(c) === selectedEdgeId) : null;
  const isPublicOutputConnection = !!selectedConnection && moduleInstances.some((m) => m.instanceName === selectedConnection.fromNodeName);

  const selectedModule = selectedNodeName
    ? moduleInstances.find((m) => m.instanceName === selectedNodeName) || null
    : null;
  const selectedModuleNode = selectedNodeName
    ? ownerOfNode(selectedNodeName, moduleInstances) || null
    : null;
  const selectedModuleLocalNodeName = selectedModuleNode && selectedNodeName
    ? selectedNodeName.slice(selectedModuleNode.instanceName.length + 1)
    : null;

  const nodeConnections = connections.filter(
    (c) => c.fromNodeName === selectedNodeName || c.toNodeName === selectedNodeName
  );

  const refreshGraph = useEngineStore((s) => s.refreshGraph);

  const handleRename = useCallback(async () => {
    if (!node) return;
    try {
      const newName = prompt('New name:', node.name);
      if (newName && newName !== node.name) {
        await api.renameNode(node.name, newName);
        await refreshGraph();
        useEngineStore.getState().setSelectedNode(newName);
      }
    } catch {
      message.error('Rename failed');
    }
  }, [node, refreshGraph]);

  const handleDeleteNode = useCallback(async () => {
    if (!node) return;
    try {
      await api.deleteNode(node.name);
      await refreshGraph();
      useEngineStore.getState().setSelectedNode(null);
      message.success(`Deleted node: ${node.name}`);
    } catch {
      message.error('Delete failed');
    }
  }, [node, refreshGraph]);

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

  // Edge selected
  if (!node && selectedConnection) {
    return (
      <Card title="Connection" style={{ height: '100%', overflow: 'auto', fontSize: 14 }}>
        <Descriptions column={1} bordered labelStyle={descLabelStyle} contentStyle={descContentStyle}>
          <Descriptions.Item label="From">{selectedConnection.fromNodeName}</Descriptions.Item>
          <Descriptions.Item label="To">{selectedConnection.toNodeName}</Descriptions.Item>
          <Descriptions.Item label="Event">
            <Tag style={{ fontSize: 13 }}>{selectedConnection.eventName}</Tag>
          </Descriptions.Item>
        </Descriptions>
        <Space style={{ marginTop: 8 }} size="small">
          {!isPublicOutputConnection && <Button size="small" onClick={() => handleRenameConnection(selectedConnection)}>Rename</Button>}
          <Popconfirm title="Delete this connection?" onConfirm={() => handleDeleteConnection(selectedConnection)} okText="Delete" cancelText="Cancel">
            <Button size="small" danger>Delete</Button>
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
      <Card title="Module" style={{ height: '100%', overflow: 'auto', fontSize: 14 }}>
        <Descriptions column={1} bordered labelStyle={descLabelStyle} contentStyle={descContentStyle}>
          <Descriptions.Item label="Instance">{selectedModule.instanceName}</Descriptions.Item>
          <Descriptions.Item label="Module">
            <Tag style={{ fontSize: 13 }} color="purple">{selectedModule.moduleName}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Pos">
            ({selectedModule.posX.toFixed(0)}, {selectedModule.posY.toFixed(0)})
          </Descriptions.Item>
          <Descriptions.Item label="Color">{selectedModule.color || 'default'}</Descriptions.Item>
          <Descriptions.Item label="FlowID">{selectedModule.flowID || 'automatic'}</Descriptions.Item>
        </Descriptions>

        <div style={{ marginTop: 8 }}>
          <strong>Public Inputs</strong>
          {Object.entries(selectedModule.publicInputs).map(([input, realNode]) => (
            <div key={input} title={realNode}><Tag color="blue">{input}</Tag></div>
          ))}
          <strong>Public Outputs (read-only)</strong>
          {Object.entries(selectedModule.publicOutputs).map(([output, event]) => (
            <div key={output} style={{ marginTop: 4 }}>
              <Tag color="green">{output}</Tag> {event}
              <div style={{ fontSize: 12, color: '#666' }}>Publishers: {(selectedModule.outputPublishers[output] ?? []).join(', ') || 'none'}</div>
            </div>
          ))}
          <p style={{ fontSize: 12 }}>Use Connect or drag an Output handle to an Input. All publishers share one interface edge.</p>
        </div>

        {selectedModule.internalNodeNames.length > 0 && (
          <div style={{ marginTop: 8 }}>
            <strong style={{ fontSize: 14 }}>Internal:</strong>
            {selectedModule.internalNodeNames.map((n) => (
              <div key={n} style={{ fontSize: 12, marginTop: 2, color: '#666' }}>{n}</div>
            ))}
          </div>
        )}

        <Space style={{ marginTop: 8 }} size="small">
          <Button
            type="primary"
            size="small"
            onClick={() => useEngineStore.getState().setViewModule(selectedModule.instanceName)}
          >
            Enter
          </Button>
          <Popconfirm title="Delete?" onConfirm={handleDeleteModule} okText="Delete" cancelText="Cancel">
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      </Card>
    );
  }

  // Nothing selected
  if (!node) {
    return (
      <Card title="Properties" style={{ height: '100%', fontSize: 14 }}>
        <span style={{ color: '#999', fontSize: 13 }}>Select a node or connection</span>
      </Card>
    );
  }

  const isGroupType = node.classType?.startsWith('Group');
  const isParallelType = node.classType?.startsWith('Parallel');
  const isGroupNode = isGroupType || isParallelType;

  // Node selected
  return (
    <Card title="Node" style={{ height: '100%', overflow: 'auto', fontSize: 14 }}>
      <Descriptions column={1} bordered labelStyle={descLabelStyle} contentStyle={descContentStyle}>
        <Descriptions.Item label="Name">{node.name}</Descriptions.Item>
        <Descriptions.Item label="Type">
          <Tag style={{ fontSize: 13 }} color="blue">{node.classType}</Tag>
        </Descriptions.Item>
        <Descriptions.Item label="Color">
          <Tag style={{ fontSize: 13 }} color={node.color}>{node.color}</Tag>
        </Descriptions.Item>
        <Descriptions.Item label="Pos">
          ({node.posX.toFixed(0)}, {node.posY.toFixed(0)})
        </Descriptions.Item>
      </Descriptions>

      {!selectedModuleNode && <Space style={{ marginTop: 8 }} size="small">
        <Button size="small" onClick={handleRename}>Rename</Button>
        <Popconfirm title="Delete?" onConfirm={handleDeleteNode} okText="Delete" cancelText="Cancel">
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      </Space>}

      {selectedModuleNode && selectedModuleLocalNodeName && (
        <div style={{ marginTop: 8 }}>
          <strong style={{ fontSize: 14 }}>Module:</strong>
          <div style={{ marginTop: 4, display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
            <Tag style={{ fontSize: 12 }} color="purple">{selectedModuleNode.moduleName}</Tag>
            <Tag style={{ fontSize: 12 }}>{selectedModuleLocalNodeName}</Tag>
            <span style={{ fontSize: 12 }}>Editing this module template affects all its instances. External connections use public interfaces only.</span>
          </div>
        </div>
      )}

      {isGroupNode && (
        <div style={{ marginTop: 8 }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4 }}>
            <strong style={{ fontSize: 14 }}>
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
                Add
              </Button>
            )}
          </div>
          {(node.groupDefs || []).map((def, i) => (
            <div key={i} style={{ marginTop: 4, display: 'flex', alignItems: 'center', gap: 4, flexWrap: 'wrap' }}>
              <Input
                addonBefore="Start"
                key={`${node.name}-gd-${i}-start`}
                defaultValue={def.startNode}
                size="small"
                style={{ fontSize: 12, flex: 1, minWidth: 50 }}
                onBlur={(e) => {
                  if (e.target.value !== def.startNode) {
                    const newDefs = [...(node.groupDefs || [])];
                    newDefs[i] = { ...newDefs[i], startNode: e.target.value };
                    handleGroupDefChange(node.name, newDefs);
                  }
                }}
                onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
              />
              <span style={{ fontSize: 14 }}>→</span>
              <Input
                addonBefore="End"
                key={`${node.name}-gd-${i}-end`}
                defaultValue={def.endEvent}
                size="small"
                style={{ fontSize: 12, flex: 1, minWidth: 50 }}
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
                  size="small"
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
        <div style={{ marginTop: 8 }}>
          <strong style={{ fontSize: 14 }}>Events:</strong>
          {node.eventDescriptions.map((ed) => (
            <div key={ed.index} style={{ marginTop: 4, fontSize: 13, display: 'flex', alignItems: 'center', gap: 4 }}>
              <Tag style={{ fontSize: 12 }}>{ed.index}</Tag>
              <span>→</span>
              <Input
                key={`${node.name}-ev-${ed.index}-${ed.description}`}
                defaultValue={ed.description}
                size="small"
                style={{ fontSize: 12, flex: 1 }}
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
        <div style={{ marginTop: 8 }}>
          <strong style={{ fontSize: 14 }}>Connections:</strong>
          {nodeConnections.map((c) => (
            <div key={`${c.fromNodeName}-${c.toNodeName}-${c.eventName}`} style={{ fontSize: 13, marginTop: 3 }}>
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
