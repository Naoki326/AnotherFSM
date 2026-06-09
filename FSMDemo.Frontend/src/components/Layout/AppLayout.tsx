import { Layout as AntLayout } from 'antd';
import { ReactFlowProvider } from '@xyflow/react';
import { ExecutionToolbar } from '../ExecutionToolbar/ExecutionToolbar';
import { FlowEditor } from '../FlowEditor/FlowEditor';
import { NodePalette } from '../NodePalette/NodePalette';
import { PropertyPanel } from '../PropertyPanel/PropertyPanel';
import { useSignalR } from '../../hooks/useSignalR';
import { useEngineStore } from '../../stores/engineStore';
import { useCallback } from 'react';

const { Sider, Content } = AntLayout;

export function AppLayout() {
  useSignalR();

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
          padding: '0 24px',
          height: 'auto',
          lineHeight: 'unset',
          borderBottom: '1px solid #f0f0f0',
        }}
      >
        <div style={{ fontWeight: 600, fontSize: 36, padding: '14px 0' }}>
          AnotherFSM Editor
        </div>
        <ExecutionToolbar />
      </AntLayout.Header>
      <AntLayout>
        <Sider
          width={280}
          style={{ background: '#fff', borderRight: '1px solid #f0f0f0', padding: 16 }}
        >
          <NodePalette />
        </Sider>
        <Content style={{ position: 'relative' }}>
          <ReactFlowProvider>
            <FlowEditor onNodeSelect={handleNodeSelect} />
          </ReactFlowProvider>
        </Content>
        <Sider
          width={420}
          style={{ background: '#fff', borderLeft: '1px solid #f0f0f0', padding: 16 }}
        >
          <PropertyPanel />
        </Sider>
      </AntLayout>
    </AntLayout>
  );
}
