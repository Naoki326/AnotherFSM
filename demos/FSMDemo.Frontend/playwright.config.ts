import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './test/browser',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  workers: 1,
  fullyParallel: false,
  reporter: [['list'], ['json', { outputFile: process.env.RUNTIME_TEST_REPORT || '../../.scratch/issue-7/browser-results.json' }]],
  outputDir: '../../.scratch/issue-7/playwright',
  use: { actionTimeout: 10_000, baseURL: 'http://127.0.0.1:5180/AnotherFSM/', trace: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { browserName: 'chromium', channel: 'chrome' } }],
});
