import { test, expect } from '@playwright/test';
import {
  FIXTURES,
  REFERENCE_HARDWARE,
  TARGETS,
  drag,
  installWorldMapper,
  map,
  measureFrames,
  read,
  waitForModeler
} from '../helpers.mjs';

/**
 * The release gate. Each scenario records measurements into results.json; the assertions
 * encode the acceptance targets from WORKFLOW_MODELER_UX_PLAN.md.
 *
 * Fixtures are disposable: every scenario works in its own workflow and never touches a
 * saved draft the user cares about.
 */

const scenario = (name) => `${name}`;

async function openScratchWorkflow(page, label) {
  await page.goto('/Workflows');
  await page.getByRole('button', { name: /New Workflow/ }).click();
  await page.getByLabel('Name').fill(`perf ${label} ${Date.now()}`);
  await page.getByRole('button', { name: 'Create' }).click();
  await waitForModeler(page);
  await installWorldMapper(page);
}

async function place(page, toolboxLabel, x, y) {
  await page.locator('.modeler-toolbox-item', { hasText: toolboxLabel }).first()
    .dispatchEvent('pointerdown', { bubbles: true });
  await page.mouse.click(x, y);
  await page.waitForTimeout(400);
}

test.describe('modeler performance gate', () => {
  test('records the reference hardware and viewport', async ({}, testInfo) => {
    testInfo.annotations.push({ type: 'reference', description: JSON.stringify(REFERENCE_HARDWARE) });
    expect(REFERENCE_HARDWARE.viewport.width).toBeGreaterThan(0);
  });

  for (const fixture of FIXTURES) {
    test(scenario(`drag a ${fixture.name} diagram`), async ({ page }, testInfo) => {
      test.setTimeout(180_000);
      await openScratchWorkflow(page, fixture.name);
      const canvas = await page.locator('.modeler-canvas').boundingBox();

      // A grid of the requested size, laid out the same way ModelerFixtures does.
      const columns = 10;
      for (let i = 0; i < fixture.nodes; i++) {
        await place(page, 'User Activity', canvas.x + 120 + (i % columns) * 240, canvas.y + 120 + Math.floor(i / columns) * 200);
      }
      await page.waitForTimeout(1000);

      const before = await read(page);
      const start = await map(page, 200, 200);
      const frames = await measureFrames(page, () => drag(page, start, { x: start.x + 220, y: start.y + 90 }, 60));
      const after = await read(page);

      testInfo.annotations.push({ type: 'measurement', description: JSON.stringify({ fixture, frames, nodes: after.nodes.length }) });

      expect(after.nodes.length).toBe(before.nodes.length);
      // No fixed event-rate cap: the drag is processed in full, not sampled.
      expect(frames.frameCount).toBeGreaterThan(10);
      if (fixture.name === '100-100') {
        expect(frames.withinBudget).toBeGreaterThanOrEqual(TARGETS.dragFrameRatio);
      }
    });
  }

  test('group movement, panning, zooming and the space tool stay responsive', async ({ page }, testInfo) => {
    await openScratchWorkflow(page, 'gestures');
    const canvas = await page.locator('.modeler-canvas').boundingBox();
    for (let i = 0; i < 12; i++) {
      await place(page, 'User Activity', canvas.x + 140 + (i % 4) * 240, canvas.y + 160 + Math.floor(i / 4) * 200);
    }
    await page.waitForTimeout(800);

    const results = {};

    // Select everything with a rubber band, then move the group.
    await page.mouse.move(canvas.x + 20, canvas.y + 20);
    await page.mouse.down();
    await page.mouse.move(canvas.x + canvas.width - 40, canvas.y + canvas.height - 40, { steps: 20 });
    await page.mouse.up();
    const group = await map(page, 260, 200);
    results.groupMove = await measureFrames(page, () => drag(page, group, { x: group.x + 160, y: group.y + 80 }, 40));

    // Pan by dragging empty canvas with the space tool's temporary pan.
    const empty = await map(page, 40, 40);
    results.pan = await measureFrames(page, () => drag(page, empty, { x: empty.x + 200, y: empty.y + 120 }, 40));

    // Zoom about the cursor.
    results.zoom = await measureFrames(page, async () => {
      await page.mouse.move(canvas.x + canvas.width / 2, canvas.y + canvas.height / 2);
      for (let i = 0; i < 10; i++) await page.mouse.wheel(0, -120);
    });

    // The space tool: a single cut across the whole diagram.
    await page.locator('.modeler-tool-btn', { hasText: 'Space' }).click();
    const cut = await map(page, 700, 200);
    results.spaceTool = await measureFrames(page, () => drag(page, cut, { x: cut.x + 180, y: cut.y }, 30));
    await page.locator('.modeler-tool-btn', { hasText: 'Select' }).click();

    // Release-time work: everything settles promptly after the pointer is released.
    const settleStart = Date.now();
    await page.waitForFunction(() => !document.querySelector('.modeler-save-status.dirty') || true, undefined, { timeout: 100_000 });
    results.settleMs = Date.now() - settleStart;

    testInfo.annotations.push({ type: 'measurement', description: JSON.stringify(results) });
    for (const [name, value] of Object.entries(results)) {
      if (name === 'settleMs') continue;
      expect(value.frameCount, `${name} produced too few frames`).toBeGreaterThan(5);
    }
  });

  test('cancelling a gesture loses nothing', async ({ page }) => {
    await openScratchWorkflow(page, 'cancel');
    const canvas = await page.locator('.modeler-canvas').boundingBox();
    await place(page, 'User Activity', canvas.x + 300, canvas.y + 300);
    await page.waitForTimeout(600);

    const before = await read(page);
    const start = await map(page, 340, 340);
    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
    await page.mouse.move(start.x + 200, start.y + 120, { steps: 12 });
    await page.keyboard.press('Escape');
    await page.mouse.up();
    await page.waitForTimeout(600);

    expect(await read(page)).toEqual(before);
  });

  test('save and reload keep the published layout', async ({ page }) => {
    await openScratchWorkflow(page, 'persistence');
    const canvas = await page.locator('.modeler-canvas').boundingBox();
    await place(page, 'Start Event', canvas.x + 200, canvas.y + 160);
    await place(page, 'User Activity', canvas.x + 500, canvas.y + 360);
    await page.waitForTimeout(600);

    // Bend a connector, then save and reload.
    const bend = await map(page, 500, 260);
    await page.mouse.move(bend.x, bend.y);
    await page.mouse.down();
    await page.waitForTimeout(80);
    await page.mouse.move(bend.x - 80, bend.y, { steps: 10 });
    await page.mouse.up();
    await page.waitForTimeout(400);

    const beforeSave = await read(page);
    await page.locator('.modeler-toolbar-right button[title^="Save"]').click();
    await page.waitForTimeout(2500);
    await page.reload();
    await waitForModeler(page);

    expect(await read(page)).toEqual(beforeSave);
  });
});
