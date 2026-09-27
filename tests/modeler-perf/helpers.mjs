/**
 * Shared helpers for the modeler performance gate.
 *
 * The gate drives the real modeler in a browser. It never asserts on native routing
 * timings: those live in Argent.WebComponents.Tests. What is measured here is what only a
 * browser can show — pointer-to-render latency, frame intervals and release-time work.
 */

export const REFERENCE_HARDWARE = {
  // Fill in the machine the recorded numbers came from. Every result file records this.
  cpu: process.env.PERF_CPU ?? 'unset',
  cores: process.env.PERF_CORES ?? 'unset',
  memory: process.env.PERF_MEMORY ?? 'unset',
  os: `${process.platform} ${process.version ? '' : ''}`.trim(),
  browserChannel: 'chromium | firefox',
  viewport: { width: 1600, height: 1000 },
  dpr: 1
};

export const TARGETS = {
  // Normal editing follows a 60 Hz display with no fixed event-rate cap.
  frameBudgetMs: 16.7,
  // At least 95% of drag frames on the 100 node fixture stay inside one frame.
  dragFrameRatio: 0.95,
  // Under 4x slowdown, pointer-to-render latency stays below this.
  throttledPointerToRenderMs: 100
};

/** Fixture sizes the gate runs. They match ModelerFixtures in the test project. */
export const FIXTURES = [
  { name: '25-25', nodes: 25, connections: 25 },
  { name: '100-100', nodes: 100, connections: 100 },
  { name: '250-500', nodes: 250, connections: 500 },
  { name: '500-500', nodes: 500, connections: 500 }
];

/** Waits for the Blazor WebAssembly modeler to finish its first render. */
export async function waitForModeler(page) {
  await page.waitForSelector('.modeler-canvas svg', { timeout: 60_000 });
  await page.waitForFunction(
    () => document.querySelectorAll('.modeler-canvas .modeler-node, .modeler-canvas .modeler-conn').length > 0,
    undefined,
    { timeout: 60_000 }
  );
  await page.waitForTimeout(500);
}

/**
 * Maps world coordinates to page coordinates using the live SVG viewBox, so a script can
 * aim at a specific bend or node regardless of the current zoom.
 */
export async function installWorldMapper(page) {
  await page.evaluate(() => {
    const svg = document.querySelector('.modeler-canvas svg');
    const canvas = document.querySelector('.modeler-canvas');
    window.__argentMap = (wx, wy) => {
      const [vx, vy, vw, vh] = svg.getAttribute('viewBox').split(/\s+/).map(Number);
      const r = canvas.getBoundingClientRect();
      return { x: r.left + (wx - vx) / vw * r.width, y: r.top + (wy - vy) / vh * r.height };
    };
    window.__argentRead = () => ({
      nodes: Array.from(document.querySelectorAll('.modeler-node')).map((g) => g.getAttribute('transform')),
      paths: Array.from(document.querySelectorAll('.modeler-conn-path')).map((p) => p.getAttribute('d'))
    });
  });
}

export const map = (page, x, y) => page.evaluate(([a, b]) => window.__argentMap(a, b), [x, y]);
export const read = (page) => page.evaluate(() => window.__argentRead());

/**
 * Records the interval between consecutive animation frames for the duration of `action`,
 * plus how long the action took to become visible. Pointer-to-render latency is measured
 * as the time from the last pointer event of a step to the frame that shows its result.
 */
export async function measureFrames(page, action) {
  await page.evaluate(() => {
    window.__argentFrames = [];
    window.__argentLast = performance.now();
    window.__argentRunning = true;
    const tick = (now) => {
      if (!window.__argentRunning) return;
      window.__argentFrames.push(now - window.__argentLast);
      window.__argentLast = now;
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  });

  const started = Date.now();
  await action();
  const elapsed = Date.now() - started;

  return page.evaluate((total) => {
    window.__argentRunning = false;
    const frames = window.__argentFrames.slice(1);
    const sorted = [...frames].sort((a, b) => a - b);
    const percentile = (p) => sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * p))] ?? 0;
    return {
      totalMs: total,
      frameCount: frames.length,
      withinBudget: frames.filter((f) => f <= 16.7).length / Math.max(1, frames.length),
      median: percentile(0.5),
      p95: percentile(0.95),
      worst: sorted[sorted.length - 1] ?? 0
    };
  }, elapsed);
}

/** Drags from one page point to another in `steps` increments, like a real pointer. */
export async function drag(page, from, to, steps = 30) {
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  await page.waitForTimeout(50);
  await page.mouse.move(to.x, to.y, { steps });
  await page.mouse.up();
}
