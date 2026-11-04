import { Alert, Button, Layout as AntLayout, Spin, Tag } from 'antd';
import { ReactFlowProvider } from '@xyflow/react';
import { ExecutionToolbar } from '../ExecutionToolbar/ExecutionToolbar';
import { FlowEditor } from '../FlowEditor/FlowEditor';
import { NodePalette } from '../NodePalette/NodePalette';
import { PropertyPanel } from '../PropertyPanel/PropertyPanel';
import { useRuntimeEvents } from '../../hooks/useRuntimeEvents';
import { runtimeConfigurationError, runtimeMode } from '../../runtime/index';
import { useEngineStore } from '../../stores/engineStore';
import { useCallback, useEffect } from 'react';

const { Sider, Content } = AntLayout;

export function AppLayout() {
  useRuntimeEvents();
  const phase = useEngineStore((s) => s.runtimePhase);
  const runtimeError = useEngineStore((s) => s.runtimeError);
  const graphError = useEngineStore((s) => s.graphError);
  const importing = useEngineStore((s) => s.importing);
  const retry = useCallback(async () => {
    await useEngineStore.getState().releaseRuntime();
    await useEngineStore.getState().initializeRuntime();
  }, []);
  useEffect(() => {
    if (runtimeConfigurationError) return;
    void useEngineStore.getState().initializeRuntime();
    return () => { void useEngineStore.getState().releaseRuntime(); };
  }, []);

  const handleNodeSelect = useCallback((nodeName: string | null) => {
    if (nodeName) {
      useEngineStore.getState().setSelectedNode(nodeName);
    }
  }, []);

  return (
    <AntLayout style={{ height: '100vh' }}>
      <AntLayout.Header
        style={{
          background: '#fff',
          padding: '0 12px',
          height: 'auto',
          lineHeight: 'unset',
          borderBottom: '1px solid #f0f0f0',
        }}
      >
        <div style={{ fontWeight: 600, fontSize: 18, padding: '7px 0' }}>
          AnotherFSM Editor <Tag aria-label="Runtime mode">{runtimeMode.toUpperCase()}</Tag>
        </div>
        {(runtimeConfigurationError || runtimeError) && <Alert type="error" showIcon title={runtimeConfigurationError || runtimeError}
          action={!runtimeConfigurationError && <Button onClick={() => { void retry(); }}>Retry initialization</Button>} />}
        {phase === 'loading' && !runtimeConfigurationError && <div role="status"><Spin size="small" /> Initializing runtime…</div>}
        {graphError && phase === 'ready' && <Alert type="error" showIcon title={`Cannot refresh graph: ${graphError}`}
          action={<Button onClick={() => { void useEngineStore.getState().refreshGraph().catch(() => { /* Error is displayed above. */ }); }}>Retry refresh</Button>} />}
        {phase === 'ready' && <ExecutionToolbar />}
      </AntLayout.Header>
      {phase === 'ready' && <AntLayout style={importing ? { pointerEvents: 'none', opacity: 0.6 } : undefined} aria-busy={importing}>
        <Sider
          width={200}
          style={{ background: '#fff', borderRight: '1px solid #f0f0f0', padding: 8 }}
        >
          <NodePalette />
        </Sider>
        <Content style={{ position: 'relative' }}>
          <ReactFlowProvider>
            <FlowEditor onNodeSelect={handleNodeSelect} />
          </ReactFlowProvider>
        </Content>
        <Sider
          width={300}
          style={{ background: '#fff', borderLeft: '1px solid #f0f0f0', padding: 8 }}
        >
          <PropertyPanel />
        </Sider>
      </AntLayout>}
    </AntLayout>
  );
}
