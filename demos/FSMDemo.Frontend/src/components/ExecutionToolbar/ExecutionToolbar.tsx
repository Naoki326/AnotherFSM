import { useCallback, useState } from 'react';
import { Button, Space, Tag, message, Modal, Tooltip } from 'antd';
import {
  PlayCircleOutlined,
  PauseCircleOutlined,
  CaretRightOutlined,
  StopOutlined,
  ImportOutlined,
  ExportOutlined,
  ExperimentOutlined,
  DeleteOutlined,
  HistoryOutlined,
} from '@ant-design/icons';
import { useEngineStore } from '../../stores/engineStore';
import { api } from '../../api/client';

const DEMO_SCRIPT = `module Checker {
    input Entry;
    output OK, NG;

    def Entry(Accumulate)
    {
        1->NextEvent;
        3->NG;
        Pos:(100, 200);
        Color: "light-blue";
    }

    def Exit(Idle)
    {
        1->OK;
        Pos:(300, 200);
        Color: "green";
    }

    def NextEvent as event;
    def OK as event;
    def NG as event;

    NextEvent->Entry to Exit;
}

import Checker;

def Start(Start)
{
    1->StartEvent;
    Pos:(100, 200);
    Color: "red";
    Type: Start;
    FlowID: 1;
}

def CheckA(Checker)
{
    0->CheckAOK;
    1->CheckANG;
    Pos:(400, 100);
    Color: "orange";
}

def CheckB(Checker)
{
    0->CheckBOK;
    1->CheckBNG;
    Pos:(400, 350);
    Color: "teal";
}

def End(End)
{
    1->EndEvent;
    Pos:(700, 200);
    Color: "indigo";
    Type: End;
    FlowID: 2;
}

def ErrorEnd(End)
{
    1->ErrorEndEvent;
    Pos:(700, 450);
    Color: "red";
    FlowID: 3;
}

StartEvent->Start to CheckA.Entry;
CheckAOK->CheckA to CheckB.Entry;
CheckANG->CheckA to ErrorEnd;
CheckBOK->CheckB to End;
CheckBNG->CheckB to ErrorEnd;`;

export function ExecutionToolbar() {
  const execution = useEngineStore((s) => s.execution);
  const nodes = useEngineStore((s) => s.nodes);
  const executionLog = useEngineStore((s) => s.executionLog);

  const [exportModalOpen, setExportModalOpen] = useState(false);
  const [exportScript, setExportScript] = useState('');

  const state = execution.state;
  const canStart = state === 'Idle' || state === 'Finished';
  const canPause = state === 'Running';
  const canContinue = state === 'Paused';
  const canStop = state === 'Running' || state === 'Paused';
  const recentExecutionLog = executionLog.slice(-10);

  const handleStart = useCallback(async () => {
    if (nodes.length === 0) {
      message.warning('No nodes to execute');
      return;
    }
    try {
      const store = useEngineStore.getState();
      store.clearExecutionLog();
      store.setActiveNode(null);
      store.setActiveTransition(null);
      await api.startExecution('Start', 'EndEvent');
      useEngineStore.getState().setExecution({ state: 'Running' });
    } catch {
      message.error('Failed to start execution');
    }
  }, [nodes]);

  const handleImport = useCallback(async () => {
    try {
      const script = prompt('Paste FSM script:');
      if (script) {
        const status = await api.importScript(script);
        useEngineStore.getState().setStatus(status);
        useEngineStore.getState().clearExecutionLog();
        const [nodes, conns, modules] = await Promise.all([api.getNodes(), api.getConnections(), api.getModuleInstances()]);
        useEngineStore.getState().setNodes(nodes);
        useEngineStore.getState().setConnections(conns);
        useEngineStore.getState().setModuleInstances(modules);
        try {
          const defs = await api.getModuleDefinitions();
          useEngineStore.getState().setModuleDefs(defs);
        } catch { /* ignore */ }
        message.success('Imported successfully');
      }
    } catch (e: any) {
      message.error(`Import failed: ${e.message}`);
    }
  }, []);

  const handleExport = useCallback(async () => {
    try {
      const script = await api.exportScript();
      setExportScript(script);
      setExportModalOpen(true);
    } catch {
      message.error('Export failed');
    }
  }, []);

  const handleLoadDemo = useCallback(async () => {
    try {
      const status = await api.importScript(DEMO_SCRIPT);
      useEngineStore.getState().setStatus(status);
      useEngineStore.getState().clearExecutionLog();
      const [nodes, conns, modules] = await Promise.all([api.getNodes(), api.getConnections(), api.getModuleInstances()]);
      useEngineStore.getState().setNodes(nodes);
      useEngineStore.getState().setConnections(conns);
      useEngineStore.getState().setModuleInstances(modules);
      try {
        const defs = await api.getModuleDefinitions();
        useEngineStore.getState().setModuleDefs(defs);
      } catch { /* ignore */ }
      message.success('Demo loaded successfully');
    } catch (e: any) {
      message.error(`Failed to load demo: ${e.message}`);
    }
  }, []);

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'stretch',
        justifyContent: 'center',
        gap: 4,
        padding: '7px 0',
        borderBottom: '1px solid #f0f0f0',
        background: '#fafafa',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6, flexWrap: 'wrap' }}>
        <Space size="small">
          <Button size="small" icon={<PlayCircleOutlined />} onClick={handleStart} disabled={!canStart} type="primary">Start</Button>
          <Button size="small" icon={<PauseCircleOutlined />} onClick={async () => {
            await api.pauseExecution();
            useEngineStore.getState().setExecution({ ...execution, state: 'Paused' });
          }} disabled={!canPause}>Pause</Button>
          <Button size="small" icon={<CaretRightOutlined />} onClick={async () => {
            await api.continueExecution();
            useEngineStore.getState().setExecution({ ...execution, state: 'Running' });
          }} disabled={!canContinue}>Continue</Button>
          <Button size="small" icon={<StopOutlined />} onClick={async () => {
            await api.stopExecution();
            useEngineStore.getState().setExecution({ state: 'Idle' });
          }} disabled={!canStop} danger>Stop</Button>
        </Space>

        <div style={{ width: 1, height: 16, background: '#d9d9d9', margin: '0 6px' }} />

        <Space size="small">
          <Button size="small" icon={<ExperimentOutlined />} onClick={handleLoadDemo}>Demo</Button>
          <Button size="small" icon={<ImportOutlined />} onClick={handleImport}>Import</Button>
          <Button size="small" icon={<ExportOutlined />} onClick={handleExport}>Export</Button>
        </Space>

        <div style={{ width: 1, height: 16, background: '#d9d9d9', margin: '0 6px' }} />

        <Tag style={{ fontSize: 12, padding: '2px 6px' }} color={state === 'Running' ? 'green' : state === 'Paused' ? 'orange' : 'default'}>
          {state}
        </Tag>
        {execution.currentNodeName && (
          <Tag style={{ fontSize: 12, padding: '2px 6px' }} color="blue">Active: {execution.currentNodeName}</Tag>
        )}
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 4, minHeight: 16, padding: '0 12px', overflow: 'hidden' }}>
        <Tag icon={<HistoryOutlined />} color="blue" style={{ margin: 0, fontSize: 11 }}>Flow</Tag>
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', gap: 3, overflowX: 'auto', whiteSpace: 'nowrap', paddingBottom: 1 }}>
          {recentExecutionLog.length === 0 ? (
            <span style={{ color: '#999', fontSize: 11 }}>No execution log</span>
          ) : (
            recentExecutionLog.map((entry) => (
              <Tag
                key={entry.id}
                color={entry.kind === 'enter' ? 'processing' : entry.kind === 'exit' ? 'default' : 'purple'}
                style={{ margin: 0, fontSize: 11 }}
              >
                <span style={{ opacity: 0.65, marginRight: 3 }}>{entry.createdAt}</span>
                {entry.message}
              </Tag>
            ))
          )}
        </div>
        <Tooltip title="Clear log">
          <Button
            size="small"
            icon={<DeleteOutlined />}
            disabled={executionLog.length === 0}
            onClick={() => useEngineStore.getState().clearExecutionLog()}
          />
        </Tooltip>
      </div>

      <Modal
        title="Exported Script"
        open={exportModalOpen}
        onCancel={() => setExportModalOpen(false)}
        footer={[
          <Button key="copy" type="primary" onClick={() => {
            navigator.clipboard.writeText(exportScript);
            message.success('Script copied to clipboard');
          }}>
            Copy to Clipboard
          </Button>,
          <Button key="close" onClick={() => setExportModalOpen(false)}>
            Close
          </Button>,
        ]}
        width={700}
      >
        <textarea readOnly value={exportScript}
          style={{ width: '100%', height: 400, fontFamily: 'monospace', fontSize: 11, padding: 6, border: '1px solid #d9d9d9', borderRadius: 4, resize: 'vertical', background: '#fafafa' }}
        />
      </Modal>
    </div>
  );
}
