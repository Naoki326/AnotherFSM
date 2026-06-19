import { useCallback } from 'react';
import { Card, Typography, message } from 'antd';
import { api } from '../../api/client';
import { useEngineStore } from '../../stores/engineStore';

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
        const offset = Math.random() * 200;
        await api.createNode({
          type: nodeType,
          name: nodeType,
          posX: 100 + offset,
          posY: 100 + offset,
          color: nodeColor,
        });
        const nodes = await api.getNodes();
        useEngineStore.getState().setNodes(nodes);
        onAddNode?.();
      } catch {
        message.error(`Failed to create ${nodeType} node`);
      }
    },
    [onAddNode]
  );

  const handleModuleClick = useCallback(
    async (moduleName: string) => {
      try {
        const offset = Math.random() * 200;
        const count = moduleInstances.filter((m) => m.moduleName === moduleName).length;
        const instanceName = `${moduleName}${count + 1}`;
        await api.createModuleInstance({
          moduleName,
          instanceName,
          posX: 100 + offset,
          posY: 100 + offset,
        });
        const [nodes, mods, conns] = await Promise.all([api.getNodes(), api.getModuleInstances(), api.getConnections()]);
        useEngineStore.getState().setNodes(nodes);
        useEngineStore.getState().setModuleInstances(mods);
        useEngineStore.getState().setConnections(conns);
        try {
          const defs = await api.getModuleDefinitions();
          useEngineStore.getState().setModuleDefs(defs);
        } catch { /* ignore */ }
        onAddNode?.();
        message.success(`Created module instance: ${instanceName}`);
      } catch (e: any) {
        message.error(`Failed to create module instance: ${e.message}`);
      }
    },
    [onAddNode, moduleInstances]
  );

  return (
    <Card title="Node Types" size="small" style={{ height: '100%', overflow: 'auto', fontSize: 14 }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        {modules.length > 0 && (
          <>
            <Text style={{ fontSize: 14, fontWeight: 600, color: '#333' }}>Modules</Text>
            {modules.map((m) => (
              <div
                key={m.moduleName}
                draggable
                onDragStart={(e) => handleModuleDragStart(e, m.moduleName)}
                onClick={() => handleModuleClick(m.moduleName)}
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
        {NODE_TYPES.map((nt) => (
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
    </Card>
  );
}
