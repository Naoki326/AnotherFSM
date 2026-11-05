import { test, expect, type Page } from '@playwright/test';

const requests = new WeakMap<Page, string[]>();
test.beforeEach(({ page }, info) => {
  if (!info.title.startsWith('wasm:')) return;
  const urls: string[] = [];
  requests.set(page, urls);
  page.on('request', request => { if (/\/api\/|\/hubs\//.test(request.url())) urls.push(request.url()); });
});
test.afterEach(({ page }, info) => {
  if (info.title.startsWith('wasm:')) expect(requests.get(page)).toEqual([]);
});

async function call(page: Page, operation: string, ...args: unknown[]) {
  return page.evaluate(async ({ operation, args }) => {
    const runtime = (window as unknown as { contractRuntime: Record<string, (...args: unknown[]) => Promise<unknown>> }).contractRuntime;
    return runtime[operation](...args);
  }, { operation, args });
}

async function events(page: Page): Promise<Array<{ type: string; nodeName?: string; state?: string }>> {
  return page.evaluate(() => (window as unknown as { contractEvents: Array<{ type: string; nodeName?: string; state?: string }> }).contractEvents);
}

for (const mode of ['wasm', 'api']) {
  test(`${mode}: repeated initialization and subscription cleanup do not duplicate notifications`, async ({ page }) => {
    await page.goto(`test/runtime.html?mode=${mode}`);
    await expect(page.locator('#result')).toContainText('"ready":true');
    await call(page, 'initialize');
    await call(page, 'initialize');
    await page.evaluate(() => {
      const runtime = (window as unknown as { contractRuntime: { subscribe: (listener: (e: unknown) => void) => () => void } }).contractRuntime;
      Object.assign(window, { leakedNotifications: 0 });
      for (let i = 0; i < 3; ++i) {
        const unsubscribe = runtime.subscribe(() => { (window as unknown as { leakedNotifications: number }).leakedNotifications++; });
        unsubscribe();
      }
    });
    for (let run = 0; run < 2; ++run) {
      await call(page, 'importScript', 'def Once(End) { 1->EndEvent; }');
      await page.evaluate(() => { (window as unknown as { contractEvents: unknown[] }).contractEvents = []; });
      await call(page, 'startExecution', 'Once', 'EndEvent');
      await expect.poll(async () => (await events(page)).some(e => e.type === 'StateChanged' && e.state === 'Finished')).toBe(true);
      expect((await events(page)).filter(e => e.type === 'NodeActivated' && e.nodeName === 'Once')).toHaveLength(1);
      expect(await page.evaluate(() => (window as unknown as { leakedNotifications: number }).leakedNotifications)).toBe(0);
      await call(page, 'dispose');
      if (run === 0) await call(page, 'initialize');
    }
  });

  test(`${mode}: execution remains responsive and isolates notifications across stop and import`, async ({ page }) => {
    await page.goto(`test/runtime.html?mode=${mode}`);
    await expect(page.locator('#result')).toContainText('"ready":true');
    await call(page, 'importScript', 'def Start(Start) { 1->Go; } def Waiting(Idle) { 1->Done; } def End(End) { 1->EndEvent; } Go -> Start to Waiting; Done -> Waiting to End;');
    expect(await call(page, 'getExecutionStatus')).toMatchObject({ state: 'Idle' });
    await call(page, 'startExecution', 'Start', 'EndEvent');
    await expect.poll(async () => (await events(page)).some(e => e.type === 'NodeActivated' && e.nodeName === 'Waiting')).toBe(true);
    await call(page, 'pauseExecution');
    expect(await call(page, 'getExecutionStatus')).toMatchObject({ state: 'Paused' });
    await call(page, 'continueExecution');
    await expect.poll(() => call(page, 'getExecutionStatus')).toMatchObject({ state: 'Running' });
    await call(page, 'stopExecution');
    expect(await call(page, 'getExecutionStatus')).toMatchObject({ state: 'Idle' });
    await call(page, 'importScript', 'module M { input Entry; output Done; def Entry(End) { 1->Done; } } use M as A; def End(End) { 1->EndEvent; } A.Done -> End;');
    await page.evaluate(() => { (window as unknown as { contractEvents: unknown[] }).contractEvents = []; });
    await call(page, 'startExecution', 'A.Entry', 'EndEvent');
    await expect.poll(() => call(page, 'getExecutionStatus')).toMatchObject({ state: 'Finished' });
    const completed = await events(page);
    expect(completed.filter(e => e.type === 'NodeActivated').map(e => e.nodeName)).toEqual(['A.Entry', 'End']);
    expect(completed.filter(e => e.type === 'NodeDeactivated').map(e => e.nodeName)).toEqual(['A.Entry', 'End']);
    // Replace a still-running graph: its old notification stream must not pollute the new run.
    await call(page, 'importScript', 'def Old(Idle) { 1->EndEvent; }');
    await call(page, 'startExecution', 'Old', 'EndEvent');
    await expect.poll(async () => (await events(page)).some(e => e.type === 'NodeActivated' && e.nodeName === 'Old')).toBe(true);
    await call(page, 'importScript', 'def New(End) { 1->EndEvent; }');
    await page.evaluate(() => { (window as unknown as { contractEvents: unknown[] }).contractEvents = []; });
    await call(page, 'startExecution', 'New', 'EndEvent');
    await expect.poll(() => call(page, 'getExecutionStatus')).toMatchObject({ state: 'Finished' });
    expect((await events(page)).filter(e => e.nodeName).every(e => e.nodeName === 'New')).toBe(true);
    await call(page, 'dispose');
  });
  test(`${mode}: preserves Module interfaces, actual ownership and script round trips`, async ({ page }) => {
    await page.goto(`test/runtime.html?mode=${mode}`);
    await expect(page.locator('#result')).toContainText('"ready":true');
    await call(page, 'importScript', `
      module M {
        input Entry, Retry; output Done;
        def Entry(Idle) { 1->Next; 2->Done; 3->Done; }
        def Retry(Idle) { 1->Done; }
        def Private(Idle) { 1->Next; }
        Next -> Entry to Private;
      }
      use M as A;
      def A.Observer(Idle) { 1->Observe; }
      def Start(Start) { 1->Launch; }
      def End(End) { 1->EndEvent; }
    `);
    expect(await call(page, 'getModuleDefinitions')).toEqual([{ moduleName: 'M', inputs: ['Entry', 'Retry'], outputs: ['Done'] }]);
    await call(page, 'createModuleInstance', { moduleName: 'M', instanceName: 'B', posX: 20, posY: 30 });
    await call(page, 'updateModuleInstancePosition', 'B', 101, 202);
    const instances = await call(page, 'getModuleInstances');
    expect(instances).toEqual(expect.arrayContaining([expect.objectContaining({ instanceName: 'B', posX: 101, posY: 202,
      publicInputs: { Entry: 'B.Entry', Retry: 'B.Retry' }, publicOutputs: { Done: 'B.Done' },
      outputPublishers: { Done: ['B.Entry', 'B.Retry'] }, internalNodeNames: ['B.Entry', 'B.Retry', 'B.Private'] })]));
    for (const toNodeName of ['A', 'A.Private']) {
      await expect(call(page, 'createConnection', { fromNodeName: 'Start', toNodeName, eventName: 'Launch' })).rejects.toThrow();
    }
    await call(page, 'createConnection', { fromNodeName: 'Start', toNodeName: 'A.Retry', eventName: 'Launch' });
    await call(page, 'createConnection', { fromNodeName: 'A', toNodeName: 'B.Entry', eventName: 'A.Done' });
    await call(page, 'createConnection', { fromNodeName: 'B', toNodeName: 'End', eventName: 'B.Done' });
    await expect(call(page, 'renameConnection', 'A', 'B.Entry', 'Alias', 'A.Done')).rejects.toThrow();
    const before = await call(page, 'getConnections');
    expect(before).toContainEqual({ fromNodeName: 'A', toNodeName: 'B.Entry', eventName: 'A.Done' });
    expect(await call(page, 'getNode', 'A.Entry')).toMatchObject({ eventDescriptions: expect.arrayContaining([
      { index: 1, description: 'A.Next' }, { index: 2, description: 'A.Done' }, { index: 3, description: 'A.Done' },
    ]) });
    const script = await call(page, 'exportScript');
    await call(page, 'importScript', script);
    const restored = await call(page, 'getConnections');
    expect(restored).toEqual(expect.arrayContaining(before as unknown[]));
    expect(restored).toHaveLength((before as unknown[]).length);
    expect(await call(page, 'getModuleInstances')).toEqual(expect.arrayContaining(instances as unknown[]));
    await call(page, 'deleteModuleInstance', 'A');
    expect(await call(page, 'getNode', 'A.Observer')).toMatchObject({ name: 'A.Observer' });
    await expect(call(page, 'getNode', 'A.Entry')).rejects.toThrow();
    const valid = await call(page, 'exportScript');
    for (const invalid of ['not valid DSL !', 'def Unknown(UnavailableDevice) {}', 'import External; use External as X;']) {
      await expect(call(page, 'importScript', invalid)).rejects.toThrow();
      expect(await call(page, 'exportScript')).toBe(valid);
    }
    await call(page, 'dispose');
  });

  test(`${mode}: edits ordinary nodes and branches through the runtime`, async ({ page }) => {
    await page.goto(`test/runtime.html?mode=${mode}`);
    await expect(page.locator('#result')).toContainText('"ready":true');
    await call(page, 'importScript', 'def End(End) { 1->EndEvent; }');
    await call(page, 'createNode', { type: 'Sleep', name: 'Step', posX: 2, posY: 3 });
    await call(page, 'renameNode', 'Step', 'Renamed');
    await call(page, 'updateNodePosition', 'Renamed', 123, 456);
    await call(page, 'updateNodeEvent', 'Renamed', 1, 'Go');
    expect(await call(page, 'getNode', 'Renamed')).toMatchObject({ name: 'Renamed', posX: 123, posY: 456,
      eventDescriptions: expect.arrayContaining([{ index: 1, description: 'Go' }]) });
    await call(page, 'createConnection', { fromNodeName: 'Renamed', toNodeName: 'End', eventName: 'Go' });
    await call(page, 'renameConnection', 'Renamed', 'End', 'Changed', 'Go');
    expect(await call(page, 'getConnections')).toContainEqual({ fromNodeName: 'Renamed', toNodeName: 'End', eventName: 'Changed' });
    await call(page, 'deleteConnection', 'Renamed', 'End', 'Changed');
    expect(await call(page, 'getNode', 'Renamed')).not.toHaveProperty('groupDefs');
    const script = await call(page, 'exportScript');
    await call(page, 'importScript', script);
    expect(await call(page, 'getNode', 'Renamed')).toMatchObject({ posX: 123, posY: 456 });
    await call(page, 'deleteNode', 'Renamed');
    await expect(call(page, 'getNode', 'Renamed')).rejects.toThrow();
    await expect(call(page, 'deleteNode', 'Renamed')).rejects.toThrow();
    await call(page, 'dispose');
  });

  test(`${mode}: rejects removed composition without replacing the Module graph`, async ({ page }) => {
    await page.goto(`test/runtime.html?mode=${mode}`);
    await expect(page.locator('#result')).toContainText('"ready":true');
    await call(page, 'importScript', 'module M { input Entry; output Done; def Entry(End) { 1->Done; } } use M as A; def End(End) { 1->Finish; } A.Done -> End;');
    const before = await call(page, 'exportScript');
    for (const type of ['Group', 'GroupT', 'GroupTU', 'Parallel', 'ParallelT', 'ParallelTU']) {
      await expect(call(page, 'createNode', { type, name: 'Old', posX: 0, posY: 0 })).rejects.toThrow(/Module.*not equivalent/);
      for (const script of [`def Old(${type}) {}`, `module Legacy { input Old; def Old(${type}) {} } use Legacy as L;`]) {
        await expect(call(page, 'importScript', script)).rejects.toThrow(/Module.*not equivalent/);
      }
    }
    for (const script of ['def Old(Start) { [Entry->Done]; }', 'module Legacy { input Old; def Old(Start) { [Entry->Done]; } }']) {
      await expect(call(page, 'importScript', script)).rejects.toThrow(/Module.*not equivalent/);
    }
    await expect(call(page, 'createNode', { type: 'Sleep', name: 'Old', posX: 0, posY: 0, groupDefs: [] })).rejects.toThrow(/Module.*not equivalent/);
    expect(await call(page, 'exportScript')).toBe(before);
    expect(await page.evaluate(() => 'updateGroupDefs' in (window as unknown as { contractRuntime: object }).contractRuntime)).toBe(false);
    // Exercise the old transport operation itself, not just its removed TS method.
    const legacyError = await page.evaluate(async (mode) => {
      if (mode === 'api') {
        const response = await fetch('http://127.0.0.1:5079/api/nodes/A.Entry/groupdefs', {
          method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: '[]',
        });
        if (response.ok) throw new Error('Legacy API unexpectedly succeeded');
        return (await response.json()).error;
      }
      const runtime = (globalThis as unknown as { getDotnetRuntime(id: number): { getAssemblyExports(name: string): Promise<{ FSMDemo: { Browser: { BrowserExports: { Invoke(op: string, payload: string): Promise<string> } } } }> } }).getDotnetRuntime(0);
      const exports = await runtime.getAssemblyExports('FSMDemo.Browser');
      return JSON.parse(await exports.FSMDemo.Browser.BrowserExports.Invoke('updateGroupDefs', '{"name":"A.Entry","groupDefs":[]}')).error;
    }, mode);
    expect(legacyError).toMatch(/Module.*not equivalent/);
    await call(page, 'startExecution', 'A.Entry', 'Finish');
    await expect.poll(() => call(page, 'getExecutionStatus')).toMatchObject({ state: 'Finished' });
    expect((await events(page)).filter(e => e.type === 'NodeActivated').map(e => e.nodeName)).toEqual(['A.Entry', 'End']);
    await call(page, 'dispose');
  });

  test(`${mode}: imports a self-contained flow and queries the real graph`, async ({ page }) => {
    const businessRequests: string[] = [];
    page.on('request', request => {
      if (/\/api\/|\/hubs\//.test(request.url())) businessRequests.push(request.url());
    });
    await page.goto(`test/runtime.html?mode=${mode}`);
    await expect(page.locator('#result')).toContainText('"ready":true');
    await call(page, 'importScript', 'def Start(Start) { 1->Go; } def End(End) { 1->EndEvent; } Go -> Start to End;');
    const status = await call(page, 'getStatus');
    expect(status).toMatchObject({ nodeNames: ['Start', 'End'], connectionCount: 1 });
    expect(await call(page, 'getNodes')).toEqual(expect.arrayContaining([expect.objectContaining({ name: 'Start', classType: 'Start' })]));
    expect(await call(page, 'getConnections')).toEqual([{ fromNodeName: 'Start', toNodeName: 'End', eventName: 'Go' }]);
    if (mode === 'wasm') expect(businessRequests).toEqual([]);
    await call(page, 'dispose');
  });
}
