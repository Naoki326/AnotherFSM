import { test, expect } from '@playwright/test';

test('wasm: initialization failure is explicit and retry loads the real runtime without API fallback', async ({ page }) => {
  const businessRequests: string[] = [];
  page.on('request', request => { if (/\/api\/|\/hubs\//.test(request.url())) businessRequests.push(request.url()); });
  let fail = true;
  await page.route('**/_framework/dotnet.js*', async route => {
    if (fail) { fail = false; await route.abort('failed'); }
    else await route.continue();
  });
  await page.goto('./');
  await expect(page.getByLabel('Runtime mode')).toHaveText('WASM');
  const retry = page.getByRole('button', { name: 'Retry initialization' });
  await expect(retry).toBeVisible();
  await expect(page.getByRole('button', { name: 'experiment Demo', exact: true })).toHaveCount(0);
  await retry.click();
  await expect(page.getByRole('button', { name: 'experiment Demo', exact: true })).toBeVisible();
  expect(businessRequests).toEqual([]);
  await page.reload();
  await expect(page.getByRole('button', { name: 'experiment Demo', exact: true })).toBeVisible();
});
