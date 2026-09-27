import { test, expect } from '@playwright/test';

/**
 * The Worker Activity authoring path, verified in one modeler session.
 *
 * Loading the modeler costs a Blazor WebAssembly start-up, so the scenarios run in a single test
 * against a single disposable workflow rather than one navigation each. Waits are on the elements
 * under test; there are no fixed sleeps.
 *
 * Validation rules are not asserted here. They are pure functions over a definition and are
 * covered by `WorkerActivityValidationTests` in Argent.WebComponents.Tests, which is a far better
 * place for them than a browser assertion per message.
 *
 * Fixtures are disposable: the workflow is created here and deleted afterwards, so no saved draft
 * the developer cares about is touched.
 */

const WORKER = 'reports';
const SUBJECT = 'render';

// The admin pages and the modeler both require an interactive session. The shared Playwright
// config carries no storage state, so sign in as the seeded development SuperAdmin.
const ADMIN = {
  username: process.env.ARGENT_TEST_USER ?? 'alexb',
  password: process.env.ARGENT_TEST_PASSWORD ?? 'MyPassword123'
};

/** Fails fast rather than hanging: an unmatched selector is a bug, not something to wait out. */
const ACTION = { timeout: 15_000 };

const label = `zz-worker-${Date.now()}`;

async function login(page) {
  await page.goto('/Workflows');
  if (!page.url().includes('/login')) return;

  await page.getByLabel('Username').fill(ADMIN.username, ACTION);
  await page.getByLabel('Password').fill(ADMIN.password, ACTION);
  await page.getByRole('button', { name: 'Log In' }).click(ACTION);
  await page.waitForURL(/\/Workflows/, { timeout: 30_000 });
}

async function createScratchWorkflow(page) {
  await login(page);
  await page.getByRole('button', { name: /New Workflow/ }).click(ACTION);
  await page.getByLabel('Name').fill(label, ACTION);
  await page.getByRole('button', { name: 'Create' }).click(ACTION);

  // The toolbox is the first thing the modeler renders; it needs no node on the canvas yet.
  await page.waitForSelector('.modeler-toolbox-item', { timeout: 60_000 });
  await expect(page.locator('.modeler-toolbox-item', { hasText: 'Worker Activity' })).toHaveCount(1);
}

async function deleteScratchWorkflow(page) {
  page.on('dialog', (d) => d.accept());
  await page.goto('/Workflows');
  const row = page.locator('tbody tr', { hasText: label });
  if (await row.count()) {
    await row.locator('[title="Delete"]').click(ACTION);
    await expect(row).toHaveCount(0, { timeout: 15_000 });
  }
}

async function placeWorkerNode(page) {
  await page.locator('.modeler-toolbox-item', { hasText: 'Worker Activity' }).first()
    .dispatchEvent('pointerdown', { bubbles: true });
  await page.mouse.click(520, 460);

  const node = page.locator('.modeler-node-shape.node-worker');
  await expect(node).toHaveCount(1);
  // The node gets its own colour so it is distinguishable from the in-process server activities.
  await expect(node).toHaveCSS('fill', 'rgb(13, 148, 136)');
  return node;
}

async function openProperties(page) {
  await page.mouse.dblclick(520, 460);
  const panel = page.locator('.modeler-properties-body');
  await expect(panel).toBeVisible();
  return panel;
}

/**
 * A properties-panel field by its label. The `has:` option in a filter is resolved relative to
 * the matched element, not to the outer locator, so match on the field's own text instead.
 * Labels are anchored so "Worker" cannot match "Wait for worker".
 */
function field(panel, name) {
  return panel.locator('.modeler-properties-field')
    .filter({ hasText: new RegExp(`^${name}`, 'i') })
    .first();
}

async function addParameter(panel, key, value) {
  await panel.locator('.modeler-worker-param-add button').click(ACTION);
  const row = panel.locator('.modeler-worker-param-row').last();
  await row.locator('input').nth(0).fill(key, ACTION);
  await row.locator('input').nth(1).fill(value, ACTION);
  return row;
}

async function readDefinition(page) {
  await page.getByRole('button', { name: /JSON/ }).click(ACTION);
  const json = await page.locator('pre, .modeler-json, code').first().innerText();
  return JSON.parse(json);
}

test.describe('worker activity authoring', () => {
  test('a worker node is placed, configured and serialised', async ({ page }) => {
    await createScratchWorkflow(page);

    try {
      await placeWorkerNode(page);
      const panel = await openProperties(page);

      await field(panel, 'Worker').locator('input').fill(WORKER, ACTION);
      await field(panel, 'Subject').locator('input').fill(SUBJECT, ACTION);
      await field(panel, 'Timeout').locator('input').fill('300', ACTION);
      await addParameter(panel, 'customer', '{{CustomerName}}');
      await expect(panel.locator('.modeler-worker-param-row')).toHaveCount(1);

      // Parameter rows can be added and removed again.
      await addParameter(panel, 'format', 'pdf');
      await expect(panel.locator('.modeler-worker-param-row')).toHaveCount(2);
      await panel.locator('.modeler-worker-param-row').last().locator('button').click(ACTION);
      await expect(panel.locator('.modeler-worker-param-row')).toHaveCount(1);

      // The switch hides its checkbox behind a styled slider, so drive it the way a user does.
      const toggleField = field(panel, 'Wait for worker');
      const toggle = toggleField.locator('input[type="checkbox"]');
      await expect(toggle).not.toBeChecked();
      await toggleField.locator('.modeler-properties-switch-slider').click(ACTION);
      await expect(toggle).toBeChecked();

      const definition = await readDefinition(page);
      const node = definition.nodes.find((n) => n.$type === 'worker');

      expect(node).toBeTruthy();
      expect(node.workerName).toBe(WORKER);
      expect(node.subject).toBe(SUBJECT);
      expect(node.timeoutSeconds).toBe(300);
      expect(node.waitForWorker).toBe(true);
      expect(node.parameters).toEqual([{ key: 'customer', value: '{{CustomerName}}' }]);
    } finally {
      await deleteScratchWorkflow(page);
    }
  });

  test('a worker is provisioned by an admin and its key shown exactly once', async ({ page }) => {
    const name = `zz-prov-${Date.now()}`;

    try {
      await login(page);
      await page.goto('/Admin/Workers');

      await page.locator('#NewWorkerName').fill(name);
      await page.getByRole('button', { name: /Provision/ }).click(ACTION);

      // The key is on this response only.
      const key = page.locator('code.select-all').first();
      await expect(key).toBeVisible();
      expect((await key.innerText()).trim().length).toBeGreaterThan(32);

      // Provisioned, but nothing is known about the process behind it until it reports.
      const row = page.locator('tbody tr', { hasText: name });
      await expect(row).toContainText('not yet reported');

      // Reloading must not show the key again.
      await page.reload();
      await expect(page.locator('code.select-all')).toHaveCount(0);

      // A duplicate name is refused rather than silently taking the existing worker over.
      await page.locator('#NewWorkerName').fill(name);
      await page.getByRole('button', { name: /Provision/ }).click(ACTION);
      await expect(page.locator('[role="alert"]')).toContainText('already registered');
    } finally {
      page.once('dialog', (d) => d.accept());
      await page.goto('/Admin/Workers');
      const row = page.locator('tbody tr', { hasText: name });
      if (await row.count()) {
        await row.locator('button[title*="Delete"]').click(ACTION);
        await expect(row).toHaveCount(0, { timeout: 15_000 });
      }
    }
  });

  test('the admin worker surfaces render and are filterable', async ({ page }) => {
    await login(page);

    await page.goto('/Admin/Workers');
    await expect(page.locator('h1', { hasText: 'Workers' })).toBeVisible();
    // The list is driven by the registry, so it is a table or the empty state, never a blank page.
    await expect(page.locator('table, .empty-state, [class*="empty"]').first()).toBeVisible();

    await page.goto('/Admin/WorkerRequests');
    await expect(page.locator('h1', { hasText: 'Worker Requests' })).toBeVisible();

    // The state filter is populated from the enum rather than hard coded.
    const states = await page.locator('#state option').allInnerTexts();
    expect(states).toEqual([
      'All', 'Pending', 'Claimed', 'Succeeded', 'Failed', 'TimedOut', 'Cancelled'
    ]);

    const workerFilter = page.locator('#workerName');
    expect(await workerFilter.locator('option').first().innerText()).toBe('All');
  });
});
