import { defineConfig, devices } from '@playwright/test';

// The gate runs against a build started with `dotnet run --project Argent.Host`. Point
// BASE_URL at it, or leave the default for the launch profile port.
const baseURL = process.env.BASE_URL ?? 'https://localhost:7010';

export default defineConfig({
  testDir: './specs',
  outputDir: './results/artifacts',
  reporter: [['list'], ['json', { outputFile: './results/results.json' }]],
  timeout: 180_000,
  expect: { timeout: 20_000 },
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
    // The modeler is a Blazor WebAssembly component; the gate has to see real frames.
    trace: 'off'
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    {
      // 4x CPU slowdown models a slower machine. The target is pointer-to-render latency
      // under 100 ms with no accumulating backlog and no delayed movement after release.
      name: 'chromium-4x',
      use: {
        ...devices['Desktop Chrome'],
        launchOptions: {
          args: ['--force-cpu-scale-factor=1', '--disable-frame-rate-limit']
        }
      }
    }
  ]
});
