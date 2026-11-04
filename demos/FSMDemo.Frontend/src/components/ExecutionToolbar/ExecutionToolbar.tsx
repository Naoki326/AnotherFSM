import { useCallback, useState } from 'react';
import { Alert, Button, Input, Space, Tag, message, Modal, Tooltip } from 'antd';
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

import DEMO_SCRIPT from '../../demo.fsm?raw';

export function ExecutionToolbar() {
  const execution = useEngineStore((s) => s.execution);
  const nodes = useEngineStore((s) => s.nodes);
  const executionLog = useEngineStore((s) => s.executionLog);

  const [exportModalOpen, setExportModalOpen] = useState(false);
  const [exportScript, setExportScript] = useState('');
  const [importModalOpen, setImportModalOpen] = useState(false);
  const [importScript, setImportScript] = useState('');
  const [importError, setImportError] = useState('');
  const [importing, setImporting] = useState(false);

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

  const handleImport = async () => {
    setImporting(true);
    setImportError('');
    try {
      await useEngineStore.getState().importScript(importScript);
      setImportModalOpen(false);
      message.success('Imported successfully');
    } catch (e) {
      setImportError(e instanceof Error ? e.message : 'Import failed');
    } finally {
      setImporting(false);
    }
  };

  const handleExport = useCallback(async () => {
    try {
      const script = await api.exportScript();
      setExportScript(script);
      setExportModalOpen(true);
    } catch (e) {
      message.error(e instanceof Error ? e.message : 'Export failed', 8);
    }
  }, []);

  const handleLoadDemo = useCallback(async () => {
    try {
      await useEngineStore.getState().importScript(DEMO_SCRIPT);
      message.success('Demo loaded successfully');
    } catch (e) {
      message.error(e instanceof Error ? e.message : 'Failed to load demo');
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
          <Button size="small" icon={<ImportOutlined />} onClick={() => { setImportError(''); setImportModalOpen(true); }}>Import</Button>
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

      <Modal title="Import FSM script" open={importModalOpen} onCancel={() => setImportModalOpen(false)}
        onOk={handleImport} okText="Import script" confirmLoading={importing} okButtonProps={{ disabled: !importScript.trim() }} width={800}>
        <p>Paste new DSL or choose a file. Import replaces the current graph. Module Instances use use … as … and qualified Output events.</p>
        <input aria-label="Import script file" type="file" accept=".fsm,.txt" onChange={async (e) => {
          const file = e.target.files?.[0];
          if (file) setImportScript(await file.text());
          e.target.value = '';
        }} />
        <Input.TextArea aria-label="FSM script" value={importScript} onChange={(e) => setImportScript(e.target.value)}
          rows={18} style={{ fontFamily: 'monospace', marginTop: 8 }} />
        {importError && <Alert type="error" showIcon title={importError} style={{ marginTop: 8 }} />}
      </Modal>

      <Modal
        title="Exported Script"
        open={exportModalOpen}
        onCancel={() => setExportModalOpen(false)}
        footer={[
          <Button key="download" onClick={() => {
            const url = URL.createObjectURL(new Blob([exportScript], { type: 'text/plain' }));
            const link = document.createElement('a');
            link.href = url; link.download = 'flow.fsm'; link.click();
            URL.revokeObjectURL(url);
          }}>Download .fsm</Button>,
          <Button key="reimport" onClick={() => {
            setImportScript(exportScript); setExportModalOpen(false); setImportError(''); setImportModalOpen(true);
          }}>Re-import…</Button>,
          <Button key="copy" type="primary" onClick={async () => {
            try {
              await navigator.clipboard.writeText(exportScript);
              message.success('Script copied to clipboard');
            } catch { message.error('Clipboard unavailable; select the script or download it.'); }
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
