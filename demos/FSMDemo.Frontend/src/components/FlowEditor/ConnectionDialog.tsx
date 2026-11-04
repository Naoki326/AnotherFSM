import { useState } from 'react';
import { Alert, AutoComplete, Form, Modal, Select, message } from 'antd';
import type { ConnectionDto } from '../../api/types';
import { runtime } from '../../runtime';
import { useEngineStore } from '../../stores/engineStore';
import { connectionTargets, ownerOfNode, validateConnection } from './moduleGraph';

export function ConnectionDialog({ initial, onClose }: { initial: Partial<ConnectionDto>; onClose: () => void }) {
  const nodes = useEngineStore((s) => s.nodes);
  const modules = useEngineStore((s) => s.moduleInstances);
  const view = useEngineStore((s) => s.viewModule);
  const [source, setSource] = useState(initial.fromNodeName);
  const [target, setTarget] = useState(initial.toNodeName);
  const [event, setEvent] = useState(initial.eventName ?? '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const sourceModule = modules.find((m) => m.instanceName === source);
  const events = sourceModule
    ? Object.entries(sourceModule.publicOutputs).map(([output, value]) => ({ label: `${output} — ${value}`, value }))
    : [...new Set(nodes.find((n) => n.name === source)?.eventDescriptions.map((e) => e.description).filter(Boolean))]
      .map((value) => ({ label: value, value }));
  const sources = nodes.filter((n) => (ownerOfNode(n.name, modules)?.instanceName ?? null) === view)
    .map((n) => ({ value: n.name, label: n.name }));
  if (!view) sources.push(...modules.map((m) => ({ value: m.instanceName, label: `📦 ${m.instanceName}` })));

  const save = async () => {
    setError('');
    try {
      const connection = validateConnection({ fromNodeName: source ?? '', toNodeName: target ?? '', eventName: event }, nodes, modules, view);
      setSaving(true);
      await runtime.createConnection(connection);
      await useEngineStore.getState().refreshGraph();
      message.success('Connection created');
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create connection');
    } finally {
      setSaving(false);
    }
  };

  return <Modal title="Connect public interfaces" open onCancel={onClose} onOk={save} okText="Connect" confirmLoading={saving}>
    <p>Choose a public Output and Input. A multi-input target must name its Input. Ordinary nodes keep normal event connections.</p>
    <Form layout="vertical">
      <Form.Item label="Source">
        <Select aria-label="Connection source" showSearch value={source} options={sources} onChange={(value) => { setSource(value); setEvent(''); }} />
      </Form.Item>
      <Form.Item label={sourceModule ? 'Public Output (qualified event)' : 'Event'}>
        {sourceModule
          ? <Select aria-label="Public Output" value={event || undefined} options={events} onChange={setEvent} />
          : <AutoComplete aria-label="Connection event" value={event} options={events} onChange={setEvent} placeholder="Select a Branch event or enter an event" />}
      </Form.Item>
      <Form.Item label="Target / public Input">
        <Select aria-label="Connection target" showSearch value={target} options={connectionTargets(nodes, modules, view)} onChange={setTarget} />
      </Form.Item>
    </Form>
    {error && <Alert type="error" showIcon title={error} />}
  </Modal>;
}
