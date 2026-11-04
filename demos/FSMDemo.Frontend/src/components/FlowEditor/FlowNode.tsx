import { memo, useCallback, useEffect } from 'react';
import { Handle, Position, useUpdateNodeInternals, type NodeProps } from '@xyflow/react';
import { useEngineStore } from '../../stores/engineStore';
import { runtime as api } from '../../runtime';
import { message } from 'antd';
import type { GroupDef } from '../../api/types';

interface FlowNodeData {
  label: string;
  classType: string;
  color: string;
  isActive: boolean;
  isSelected: boolean;
  isInnerStartNode?: boolean;
  isInnerEndNode?: boolean;
  isModuleInstance?: boolean;
  publicInputs?: Record<string, string>;
  publicOutputs?: Record<string, string>;
  events: { index: number; description: string }[];
  groupDefs?: GroupDef[];
}

const NODE_COLORS: Record<string, string> = {
  red: '#f44336', pink: '#e91e63', purple: '#9c27b0', 'deep-purple': '#673ab7',
  indigo: '#3f51b5', blue: '#2196f3', 'light-blue': '#03a9f4', cyan: '#00bcd4',
  teal: '#009688', green: '#4caf50', 'light-green': '#8bc34a', lime: '#cddc39',
  yellow: '#ffeb3b', amber: '#ffc107', orange: '#ff9800', 'deep-orange': '#ff5722',
  brown: '#795548', 'blue-grey': '#607d8b', grey: '#9e9e9e',
};

export const FlowNode = memo(({ data, id }: NodeProps) => {
  const { label, classType, color, isActive, isSelected, isInnerStartNode, isInnerEndNode, isModuleInstance, events, groupDefs,
    publicInputs = {}, publicOutputs = {} } = data as unknown as FlowNodeData;
  const updateNodeInternals = useUpdateNodeInternals();
  useEffect(() => { updateNodeInternals(id); }, [id, publicInputs, publicOutputs, updateNodeInternals]);
  const bgColor = color?.startsWith('#') ? color : (NODE_COLORS[color] || '#999');
  const showEvents = !isModuleInstance && isSelected && events.length > 0;
  const isGroupNode = classType?.startsWith('Group') || classType?.startsWith('Parallel');
  const showGroupDefs = isSelected && isGroupNode && groupDefs && groupDefs.length > 0;

  const borderColor = isModuleInstance
    ? '2px dashed rgba(255,255,255,0.7)'
    : isGroupNode ? '2px dashed rgba(255,255,255,0.5)'
      : isInnerStartNode && isInnerEndNode ? '2px solid #d4b106'
      : isInnerStartNode ? '2px solid #d4b106'
      : isInnerEndNode ? '2px solid #722ed1'
      : '1px solid rgba(255,255,255,0.2)';

  const boxShadow = isActive
    ? `0 0 0 3px #fff, 0 0 0 5px ${bgColor}, 0 4px 12px rgba(0,0,0,0.3)`
    : isInnerStartNode && isInnerEndNode
      ? `0 0 0 3px #ffd666, 0 0 12px rgba(212,177,6,0.4), 0 0 0 6px rgba(114,46,209,0.3)`
      : isInnerStartNode
        ? `0 0 0 3px #ffd666, 0 0 12px rgba(212,177,6,0.4)`
        : isInnerEndNode
          ? `0 0 0 3px #b37feb, 0 0 12px rgba(114,46,209,0.4)`
          : isSelected
            ? `0 0 0 2px #1976d2, 0 2px 8px rgba(0,0,0,0.2)`
            : isModuleInstance
              ? `0 0 0 2px rgba(0,0,0,0.1), 0 3px 10px rgba(0,0,0,0.2)`
              : '0 2px 6px rgba(0,0,0,0.15)';

  const handleEventChange = useCallback(
    async (index: number, newDesc: string) => {
      try {
        await api.updateNodeEvent(id, index, newDesc);
        await useEngineStore.getState().refreshGraph();
      } catch {
        message.error('Failed to update event');
      }
    },
    [id]
  );

  const handleGroupDefChange = useCallback(
    async (index: number, field: 'startNode' | 'endEvent', value: string) => {
      try {
        const current = groupDefs || [];
        const newDefs = [...current];
        newDefs[index] = { ...newDefs[index], [field]: value };
        await api.updateGroupDefs(id, newDefs);
        await useEngineStore.getState().refreshGraph();
      } catch {
        message.error('Failed to update group definition');
      }
    },
    [id, groupDefs]
  );

  return (
    <div>
      <div
        aria-current={isActive ? 'step' : undefined}
        style={{
          padding: '10px 16px',
          borderRadius: isGroupNode ? 10 : isModuleInstance ? 8 : 6,
          background: bgColor,
          color: '#fff',
          minWidth: 120,
          textAlign: 'center',
          fontSize: 13,
          fontWeight: 500,
          boxShadow,
          transition: 'box-shadow 0.2s',
          animation: isActive ? 'fsm-active-node-pulse 1s ease-in-out infinite' : undefined,
          border: borderColor,
          cursor: isModuleInstance ? 'context-menu' : 'default',
        }}
      >
        {!isModuleInstance && <Handle id="input" type="target" position={Position.Left} style={{ background: '#555', width: 10, height: 10 }} />}
        <div style={{ fontWeight: 600, marginBottom: 2, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {label}
        </div>
        <div style={{ fontSize: 10, opacity: 0.8 }}>({classType})</div>
        {isModuleInstance && (
          <div style={{ fontSize: 9, opacity: 0.6, marginTop: 2 }}>Right-click to enter</div>
        )}
        {!isModuleInstance && <Handle id="output" type="source" position={Position.Right} style={{ background: '#555', width: 10, height: 10 }} />}
        {isModuleInstance && <div style={{ marginTop: 8, fontSize: 11 }}>
          {Object.keys(publicInputs).map((input) => <div key={`in-${input}`} style={{ position: 'relative', padding: '4px 2px', textAlign: 'left' }}>
            <Handle id={`input:${input}`} type="target" position={Position.Left} style={{ left: -16, background: '#1677ff', width: 10, height: 10 }} />
            Input: {input}
          </div>)}
          {Object.entries(publicOutputs).map(([output, event]) => <div key={`out-${output}`} title={event} style={{ position: 'relative', padding: '4px 2px', textAlign: 'right' }}>
            Output: {output}
            <Handle id={`output:${event}`} type="source" position={Position.Right} style={{ right: -16, background: '#52c41a', width: 10, height: 10 }} />
          </div>)}
        </div>}
      </div>

      {showGroupDefs && (
        <div
          style={{
            marginTop: 4,
            background: '#fffbe6',
            borderRadius: 6,
            border: '1px dashed #d4b106',
            padding: '6px 8px',
            boxShadow: '0 2px 6px rgba(0,0,0,0.1)',
            display: 'flex',
            flexDirection: 'column',
            gap: 4,
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <div style={{ fontSize: 11, color: '#ad6800', fontWeight: 600, marginBottom: 2 }}>
            {classType.startsWith('Parallel') ? 'Inner FSMs' : 'Inner FSM'}
          </div>
          {groupDefs.map((def, i) => (
            <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 11 }}>
              <input
                defaultValue={def.startNode}
                placeholder="StartNode"
                style={{
                  border: '1px solid #e8d48b',
                  borderRadius: 3,
                  padding: '1px 4px',
                  fontSize: 11,
                  width: '42%',
                  outline: 'none',
                  background: '#fffdf0',
                }}
                onBlur={(e) => {
                  if (e.target.value !== def.startNode) {
                    handleGroupDefChange(i, 'startNode', e.target.value);
                  }
                }}
                onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
              />
              <span style={{ color: '#d4b106', fontWeight: 700 }}>→</span>
              <input
                defaultValue={def.endEvent}
                placeholder="EndEvent"
                style={{
                  border: '1px solid #e8d48b',
                  borderRadius: 3,
                  padding: '1px 4px',
                  fontSize: 11,
                  width: '42%',
                  outline: 'none',
                  background: '#fffdf0',
                }}
                onBlur={(e) => {
                  if (e.target.value !== def.endEvent) {
                    handleGroupDefChange(i, 'endEvent', e.target.value);
                  }
                }}
                onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
              />
            </div>
          ))}
        </div>
      )}

      {showEvents && (
        <div
          style={{
            marginTop: 4,
            background: '#fff',
            borderRadius: 6,
            border: '1px solid #d9d9d9',
            padding: '6px 8px',
            boxShadow: '0 2px 6px rgba(0,0,0,0.1)',
            display: 'flex',
            flexDirection: 'column',
            gap: 4,
          }}
          onClick={(e) => e.stopPropagation()}
        >
          {events.map((ev) => (
            <div key={`${ev.index}:${ev.description}`} style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 12 }}>
              <span style={{ color: '#999', fontWeight: 600, minWidth: 18 }}>{ev.index}</span>
              <span style={{ color: '#999' }}>→</span>
              <input
                defaultValue={ev.description}
                style={{
                  border: '1px solid #d9d9d9',
                  borderRadius: 3,
                  padding: '2px 6px',
                  fontSize: 12,
                  width: '100%',
                  outline: 'none',
                }}
                onBlur={(e) => {
                  if (e.target.value !== ev.description) {
                    handleEventChange(ev.index, e.target.value);
                  }
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    (e.target as HTMLInputElement).blur();
                  }
                }}
              />
            </div>
          ))}
        </div>
      )}
    </div>
  );
});

FlowNode.displayName = 'FlowNode';
