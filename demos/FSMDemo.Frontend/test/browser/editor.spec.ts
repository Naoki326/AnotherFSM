import { test, expect, type Page } from '@playwright/test';

async function importScript(page: Page, script: string) {
  await page.getByRole('button', { name: 'import Import', exact: true }).click();
  await page.getByLabel('FSM script', { exact: true }).fill(script);
  await page.getByRole('button', { name: 'Import script', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Import FSM script' })).toBeHidden();
}

async function choose(page: Page, label: string, option: string) {
  const combo = page.getByRole('combobox', { name: label, exact: true });
  await combo.click();
  if (label !== 'Public Output') await combo.fill(option.replace(/^📦\s*/, '').split(' (')[0]);
  const item = page.locator('.ant-select-dropdown:not(.ant-select-dropdown-hidden)').getByTitle(option, { exact: true });
  await expect(item).toBeVisible();
  await item.click();
  await expect(combo).not.toHaveAttribute('aria-expanded', 'true');
}
async function connect(page: Page, source: string, event: string, target: string, module = false) {
  await page.getByRole('button', { name: 'Connect', exact: true }).click();
  await choose(page, 'Connection source', source);
  if (module) await choose(page, 'Public Output', event);
  else await page.getByLabel('Connection event', { exact: true }).fill(event);
  await choose(page, 'Connection target', target);
  await page.getByRole('dialog').getByRole('button', { name: 'Connect', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
}

test('wasm: full editor loop edits nodes, instances and edges, executes and round trips', async ({ page }) => {
  const businessRequests: string[] = [];
  page.on('request', request => { if (/\/api\/|\/hubs\//.test(request.url())) businessRequests.push(request.url()); });
  await page.goto('./');
  await expect(page.getByRole('button', { name: 'import Import', exact: true })).toBeVisible();
  await importScript(page, `module M { input Entry; output Done; def Entry(Idle) { 1->Done; } }
    def Start(Start) { 1->Launch; Pos:(20,20); } def End(End) { 1->EndEvent; Pos:(600,20); }`);
  for (const type of ['Group', 'Parallel']) {
    await expect(page.locator('aside').first().getByText(type, { exact: true })).toHaveCount(0);
  }
  await page.locator('aside').first().getByText('Sleep', { exact: true }).click();
  await page.locator('.react-flow__node[data-id="Sleep"]').click();
  page.once('dialog', dialog => dialog.accept('Edited'));
  await page.getByRole('button', { name: 'Rename', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="Edited"]')).toBeVisible();
  await page.getByRole('button', { name: /📦 M inputs:/ }).click();
  await page.getByLabel('Instance name (Namespace prefix allowed)').fill('A');
  await page.getByRole('button', { name: 'Create instance', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="A"]')).toBeVisible();
  await page.getByRole('button', { name: 'Fit View' }).click();
  const instance = page.locator('.react-flow__node[data-id="A"]');
  const box = await instance.boundingBox();
  if (!box) throw new Error('Module Instance is not visible');
  await page.mouse.move(box.x + box.width / 2, box.y + 15);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width / 2 + 70, box.y - 55, { steps: 12 });
  await page.mouse.up();
  await expect(page.getByText('(120, 360)', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Enter', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="A.Entry"]')).toBeVisible();
  await page.getByText('Root', { exact: true }).click();
  await connect(page, 'Start', 'Launch', 'A (Input: Entry)');
  await connect(page, '📦 A', 'Done — A.Done', 'End', true);
  await expect(page.locator('.react-flow__edge')).toHaveCount(2);
  await page.getByRole('button', { name: 'play-circle Start', exact: true }).click();
  await expect(page.getByRole('banner')).toContainText('Enter A.Entry');
  await page.getByRole('button', { name: 'pause-circle Pause', exact: true }).click();
  await expect(page.getByRole('button', { name: 'caret-right Continue', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'caret-right Continue', exact: true }).click();
  await expect(page.getByRole('banner')).toContainText('Continued');
  await page.getByRole('button', { name: 'stop Stop', exact: true }).click();
  await expect(page.getByRole('button', { name: 'play-circle Start', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'play-circle Start', exact: true }).click();
  await expect(page.getByRole('banner')).toContainText('Finished');
  await expect(page.getByRole('banner')).toContainText('Enter End');
  await page.getByRole('button', { name: 'export Export', exact: true }).click();
  const exported = await page.getByRole('dialog', { name: 'Exported Script' }).getByRole('textbox').inputValue();
  expect(exported).toContain('def Edited(Sleep)');
  expect(exported).toContain('use M as A');
  expect(exported).toContain('A.Done -> End;');
  await page.getByRole('button', { name: 'Re-import…', exact: true }).click();
  await page.getByRole('button', { name: 'Import script', exact: true }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.locator('.react-flow__node[data-id="A"]')).toBeVisible();
  await expect(page.locator('.react-flow__node[data-id="Edited"]')).toBeVisible();
  await expect(page.locator('.react-flow__edge')).toHaveCount(2);
  await page.getByRole('button', { name: 'import Import', exact: true }).click();
  await page.getByLabel('FSM script', { exact: true }).fill('def Invalid(UnavailableDevice) {}');
  await page.getByRole('button', { name: 'Import script', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="Edited"]')).toBeVisible();
  expect(businessRequests).toEqual([]);
});

test('wasm: failed import preserves the paused executor, controls and existing log', async ({ page }) => {
  await page.goto('./');
  await expect(page.getByRole('button', { name: 'import Import', exact: true })).toBeVisible();
  await importScript(page, 'def Start(Idle) { 1->EndEvent; Pos:(20,20); }');
  await page.getByRole('button', { name: 'play-circle Start', exact: true }).click();
  await expect(page.getByRole('banner')).toContainText('Enter Start');
  await page.getByRole('button', { name: 'pause-circle Pause', exact: true }).click();
  await expect(page.getByRole('button', { name: 'caret-right Continue', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'import Import', exact: true }).click();
  await page.getByLabel('FSM script', { exact: true }).fill('def Invalid(UnknownDevice) {}');
  await page.getByRole('button', { name: 'Import script', exact: true }).click();
  await expect(page.getByRole('dialog').getByRole('alert')).toBeVisible();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.getByRole('button', { name: 'caret-right Continue', exact: true })).toBeEnabled();
  await expect(page.getByRole('banner')).toContainText('Paused at Start');
  await expect(page.getByRole('banner')).toContainText('Enter Start');
  await page.getByRole('button', { name: 'stop Stop', exact: true }).click();
  await expect(page.getByRole('button', { name: 'play-circle Start', exact: true })).toBeEnabled();
});

test('wasm: rejected start keeps the existing execution log and shows the host error', async ({ page }) => {
  await page.goto('./');
  await expect(page.getByRole('button', { name: 'import Import', exact: true })).toBeVisible();
  await importScript(page, 'def Start(End) {1->EndEvent; Pos:(20,20); }');
  await page.getByRole('button', { name: 'play-circle Start', exact: true }).click();
  await expect(page.getByRole('banner')).toContainText('Finished');
  await expect(page.getByRole('banner')).toContainText('Enter Start');
  await page.locator('.react-flow__node[data-id="Start"]').click();
  page.once('dialog', dialog => dialog.accept('Other'));
  await page.getByRole('button', { name: 'Rename', exact: true }).click();
  await expect(page.locator('.react-flow__node[data-id="Other"]')).toBeVisible();
  await page.getByRole('button', { name: 'play-circle Start', exact: true }).click();
  await expect(page.getByText('Edit or execution control failed: object not found or operation not allowed', { exact: true })).toBeVisible();
  await expect(page.getByRole('banner')).toContainText('Enter Start');
  await expect(page.getByRole('banner')).toContainText('Finished');
});

test('wasm: starting records and highlights the first entered node', async ({ page }) => {
  await page.goto('./');
  await expect(page.getByRole('button', { name: 'import Import', exact: true })).toBeVisible();
  await importScript(page, 'def Start(Idle) { 1->EndEvent; Pos:(20,20); }');
  await page.getByRole('button', { name: 'play-circle Start', exact: true }).click();
  await expect(page.getByRole('banner')).toContainText('Enter Start');
  await expect(page.locator('[aria-current="step"]').filter({ hasText: 'Start' })).toBeVisible();
  await page.getByRole('button', { name: 'stop Stop', exact: true }).click();
  await expect(page.getByRole('button', { name: 'play-circle Start', exact: true })).toBeEnabled();
});
