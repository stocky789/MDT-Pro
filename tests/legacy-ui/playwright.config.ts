import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  timeout: 30_000,
  fullyParallel: true,
  use: {
    ...devices['Desktop Chrome'],
    viewport: { width: 1280, height: 720 },
  },
});
