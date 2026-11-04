import { useCallback, useState } from 'react';
import { Alert, Card, Input, Modal, Typography, message } from 'antd';
import { runtime as api } from '../../runtime';
import { useEngineStore } from '../../stores/engineStore';
import { nextInstanceName } from '../FlowEditor/moduleGraph';

const { Text } = Typography;

interface NodeTypeDef {
  type: string;
  color: string;
}

const NODE_TYPES: NodeTypeDef[] = [
  { type: 'Start', color: '#f44336' },
  { type: 'End', color: '#3f51b5' },
  { type: 'Sleep', color: '#03a9f4' },
  { type: 'Accumulate', color: '#4caf50' },
  { type: 'Idle', color: '#ff9800' },
  { type: 'Group', color: '#9c27b0' },
  { type: 'Parallel', color: '#009688' },
];

interface NodePaletteProps {
  onAddNode?: () => void;
}

export function NodePalette({ onAddNode }: NodePaletteProps) {
  const modules = useEngineStore((s) => s.moduleDefs);
  const moduleInstances = useEngineStore((s) => s.moduleInstances);
  const nodes = useEngineStore((s) => s.nodes);
  const viewModule = useEngineStore((s) => s.viewModule);
  const [creatingModule, setCreatingModule] = useState<string | null>(null);
  const [instanceName, setInstanceName] = useState('');
  const [createError, setCreateError] = useState('');
  const [creating, setCreating] = useState(false);

  const handleDragStart = useCallback(
    (e: React.DragEvent, nodeType: string, nodeColor: string) => {
      e.dataTransfer.setData('application/reactflow-type', nodeType);
      e.dataTransfer.setData('application/reactflow-color', nodeColor);
      e.dataTransfer.effectAllowed = 'move';
    },
    []
  );

  const handleModuleDragStart = useCallback(
    (e: React.DragEvent, moduleType: string) => {
      e.dataTransfer.setData('application/reactflow-module-type', moduleType);
      e.dataTransfer.effectAllowed = 'move';
    },
    []
  );

  const handleClick = useCallback(
    async (nodeType: string, nodeColor: string) => {
      try {
        if (viewModule) {
          message.info('Add template nodes through Import; this canvas edits positions, Branches and internal connections.');
          return;
        }
        const offset = Math.random() * 200;
        await api.createNode({
          type: nodeType,
          name: nodeType,
          posX: 100 + offset,
          posY: 100 + offset,
          color: nodeColor,
        });
        await useEngineStore.getState().refreshGraph();
        onAddNode?.();
      } catch {
        message.error(`Failed to create ${nodeType} node`);
      }
    },
    [onAddNode, viewModule]
  );

  const handleModuleCreate = async () => {
    if (!creatingModule || !instanceName.trim()) return;
    setCreating(true);
    setCreateError('');
    try {
      await api.createModuleInstance({ moduleName: creatingModule, instanceName: instanceName.trim(), posX: 120, posY: 360 });
      await useEngineStore.getState().refreshGraph();
      useEngineStore.getState().setSelectedNode(instanceName.trim());
      setCreatingModule(null);
      onAddNode?.();
      message.success(`Created module instance: ${instanceName.trim()}`);
    } catch (e) {
      setCreateError(e instanceof Error ? e.message : 'Failed to create module instance');
    } finally { setCreating(false); }
  };

  return (
    <Card title="Node Types" size="small" style={{ height: '100%', overflow: 'auto', fontSize: 14 }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        {viewModule && <Alert type="info" title="Template editing" description="Edits affect all instances. Return to Root to create instances or connect public interfaces." />}
        {!viewModule && modules.length > 0 && (
          <>
            <Text style={{ fontSize: 14, fontWeight: 600, color: '#333' }}>Modules</Text>
            {modules.map((m) => (
              <div
                key={m.moduleName}
                draggable
                onDragStart={(e) => handleModuleDragStart(e, m.moduleName)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.click(); }}
                onClick={() => {
                  setInstanceName(nextInstanceName(m.moduleName, moduleInstances, nodes));
                  setCreateError(''); setCreatingModule(m.moduleName);
                }}
                style={{
                  padding: '7px 9px',
                  borderRadius: 4,
                  background: 'linear-gradient(135deg, #7c4dff, #651fff)',
                  color: '#fff',
                  cursor: 'grab',
                  textAlign: 'center',
                  fontSize: 14,
                  fontWeight: 500,
                  userSelect: 'none',
                }}
              >
                <div style={{ fontSize: 14, fontWeight: 700 }}>📦 {m.moduleName}</div>
                <div style={{ fontSize: 11, opacity: 0.7, marginTop: 1 }}>
                  inputs: {m.inputs.join(', ') || '-'} | outputs: {m.outputs.join(', ') || '-'}
                </div>
              </div>
            ))}
            <div style={{ borderBottom: '1px solid #e8e8e8', margin: '2px 0' }} />
            <Text style={{ fontSize: 14, fontWeight: 600, color: '#333' }}>Basic Nodes</Text>
          </>
        )}
        {!viewModule && NODE_TYPES.map((nt) => (
          <div
            key={nt.type}
            draggable
            onDragStart={(e) => handleDragStart(e, nt.type, nt.color)}
            onClick={() => handleClick(nt.type, nt.color)}
            style={{
              padding: '7px 9px',
              borderRadius: 4,
              background: nt.color,
              color: '#fff',
              cursor: 'grab',
              textAlign: 'center',
              fontSize: 14,
              fontWeight: 500,
              userSelect: 'none',
            }}
          >
            <Text style={{ color: '#fff', fontSize: 14 }}>{nt.type}</Text>
          </div>
        ))}
      </div>
      <Modal title={`Create Module Instance — ${creatingModule ?? ''}`} open={!!creatingModule} onCancel={() => setCreatingModule(null)}
        onOk={handleModuleCreate} okText="Create instance" confirmLoading={creating} okButtonProps={{ disabled: !instanceName.trim() }}>
        <p>Public Inputs and Outputs come from the module. No event alias mapping is needed.</p>
        <label htmlFor="instance-name">Instance name (Namespace prefix allowed)</label>
        <Input id="instance-name" value={instanceName} onChange={(e) => setInstanceName(e.target.value)} onPressEnter={handleModuleCreate} />
        {createError && <Alert style={{ marginTop: 8 }} type="error" title={createError} showIcon />}
      </Modal>
    </Card>
  );
}
