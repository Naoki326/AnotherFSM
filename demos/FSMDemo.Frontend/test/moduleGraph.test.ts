import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { ModuleInstance, NodeInfo } from '../src/api/types.ts';
import { connectionEdgeId, connectionTargets, nextInstanceName, targetEndpoint, validateConnection, visibleConnections } from '../src/components/FlowEditor/moduleGraph.ts';
import { api } from '../src/api/client.ts';

const node = (name: string): NodeInfo => ({ name, classType: 'Idle', posX: 0, posY: 0, color: '', flowID: '', eventDescriptions: [{ index: 1, description: 'Next' }] });
const instance = (name: string, multi = false): ModuleInstance => ({
  instanceName: name, moduleName: 'Checker', posX: 0, posY: 0, color: 'red', flowID: name,
  internalNodeNames: [`${name}.Entry`, `${name}.Retry`, `${name}.Private`],
  publicInputs: multi ? { Entry: `${name}.Entry`, Retry: `${name}.Retry` } : { Entry: `${name}.Entry` },
  publicOutputs: { OK: `${name}.OK` }, outputPublishers: { OK: [`${name}.Entry`, `${name}.Retry`] },
});
const modules = [instance('Space.A'), instance('Space.B', true)];
const nodes = [node('Start'), node('End'), ...modules.flatMap((m) => m.internalNodeNames.map(node))];

test('single Input selects instance; multiple Inputs require explicit public Input, never private nodes', () => {
  const targets = connectionTargets(nodes, modules, null).map((t) => t.value);
  assert.deepEqual(targets, ['Start', 'End', 'Space.A', 'Space.B.Entry', 'Space.B.Retry']);
  for (const target of ['Space.B', 'Space.B.Private', 'Space.B.Unknown', 'Space.A.Private']) {
    assert.throws(() => validateConnection({ fromNodeName: 'Start', toNodeName: target, eventName: 'Next' }, nodes, modules, null), /public Input/);
  }
  assert.equal(validateConnection({ fromNodeName: 'Start', toNodeName: 'Space.A', eventName: 'Next' }, nodes, modules, null).toNodeName, 'Space.A');
});

test('qualified Outputs preserve instance and Namespace identity, do not invent aliases', () => {
  const c = { fromNodeName: 'Space.A', toNodeName: 'Space.B.Retry', eventName: 'Space.A.OK' };
  assert.deepEqual(validateConnection(c, nodes, modules, null), c);
  assert.throws(() => validateConnection({ ...c, eventName: 'OK' }, nodes, modules, null), /public Output/);
  assert.throws(() => validateConnection({ ...c, eventName: 'Space.B.OK' }, nodes, modules, null), /public Output/);
  assert.deepEqual(targetEndpoint(c.toNodeName, modules), { node: 'Space.B', handle: 'Retry' });
});

test('one interface edge for many publishers, with distinct target Inputs and self-module loop retained', () => {
  const c = { fromNodeName: 'Space.A', toNodeName: 'Space.B.Retry', eventName: 'Space.A.OK' };
  const otherInput = { ...c, toNodeName: 'Space.B.Entry' };
  const loop = { ...c, toNodeName: 'Space.A' };
  const internal = { fromNodeName: 'Space.A.Entry', toNodeName: 'Space.A.Private', eventName: 'Space.A.Next' };
  assert.deepEqual(visibleConnections([c, c, otherInput, loop, internal], modules, null), [c, otherInput, loop]);
  assert.notEqual(connectionEdgeId(c), connectionEdgeId(otherInput));
  assert.deepEqual(visibleConnections([c, internal], modules, 'Space.A'), [internal]);
});

test('internal template canvas cannot connect across public boundary', () => {
  const c = { fromNodeName: 'Space.A.Entry', toNodeName: 'Space.A.Private', eventName: 'Space.A.Next' };
  assert.deepEqual(validateConnection(c, nodes, modules, 'Space.A'), c);
  assert.throws(() => validateConnection(c, nodes, modules, null), /not accessible/);
  assert.throws(() => validateConnection({ ...c, toNodeName: 'End' }, nodes, modules, 'Space.A'), /public Input/);
  assert.throws(() => validateConnection({ ...c, fromNodeName: 'Space.A', eventName: 'Space.A.OK' }, nodes, modules, 'Space.A'), /public Output/);
});

test('ordinary nodes keep event connections and colliding instance names are skipped', () => {
  const c = { fromNodeName: 'Start', toNodeName: 'End', eventName: 'Next' };
  assert.deepEqual(validateConnection(c, nodes, modules, null), c);
  assert.equal(nextInstanceName('Checker', [instance('Checker1'), instance('Checker3')], [node('Checker2')]), 'Checker4');
});

test('HTTP client sends canonical interface names for create/delete and reports API export refusals', async () => {
  const previous = globalThis.fetch;
  const calls: { url: string; method: string; body: unknown }[] = [];
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), method: init?.method ?? 'GET', body: init?.body ? JSON.parse(String(init.body)) : undefined });
    return new Response(null, { status: 204 });
  };
  try {
    const c = { fromNodeName: 'Space.A', toNodeName: 'Space.B.Retry', eventName: 'Space.A.OK' };
    await api.createConnection(c);
    await api.deleteConnection(c.fromNodeName, c.toNodeName, c.eventName);
    assert.deepEqual(calls.map((c) => c.body), [c, c]);
    assert.deepEqual(calls.map((c) => c.method), ['POST', 'DELETE']);
    await api.createModuleInstance({ moduleName: 'Checker', instanceName: 'New', posX: 10, posY: 20 });
    assert.deepEqual(calls[2].body, { moduleName: 'Checker', instanceName: 'New', posX: 10, posY: 20 });
    await api.updateModuleInstancePosition('Space.A', 70, 80);
    assert.ok(calls[3].url.endsWith('/Space.A/position'));
    globalThis.fetch = async () => new Response(JSON.stringify({ error: 'Legacy mapping cannot be exported' }), { status: 400 });
    await assert.rejects(api.exportScript(), /Legacy mapping/);
    await assert.rejects(api.createConnection(c), /Legacy mapping/);
  } finally { globalThis.fetch = previous; }
});
