import { expect, test } from '@playwright/test';

const BASE = process.env.APP_BASE_URL ?? 'http://localhost:8080';

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE}${path}`);
  if (!res.ok) throw new Error(`${path} -> ${res.status}`);
  return res.json() as Promise<T>;
}

async function postJson(path: string, body: unknown) {
  const res = await fetch(`${BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!res.ok) throw new Error(`${path} -> ${res.status} ${await res.text()}`);
  return res.json();
}

async function waitForRun(page: import('@playwright/test').Page, runId: string) {
  const id = runId;
  await expect
    .poll(
      async () => (await getJson<{ status: string }>(`/api/runs/${id}`)).status,
      { timeout: 60_000, intervals: [500, 1000] }
    )
    .toBe('Completed');
}

test.describe('API regression lab main flow', () => {
  test.beforeAll(async () => {
    // Make the suite repeatable: drop scenarios created by previous runs and
    // re-enable everything that remains.
    const scenarios = await getJson<Array<any>>('/api/scenarios');
    for (const s of scenarios) {
      if (s.name.startsWith('e2e:')) {
        await fetch(`${BASE}/api/scenarios/${s.id}`, { method: 'DELETE' });
      } else if (!s.enabled) {
        await fetch(`${BASE}/api/scenarios/${s.id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...s, enabled: true })
        });
      }
    }
  });

  test('seeded scenarios and active rule are present', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /场景/ }).click();
    await expect(page.getByText('stable: health ping')).toBeVisible();
    await expect(page.getByText('diff: user email field changed')).toBeVisible();
    // Secret scenarios show only a reference, never a value.
    await expect(page.getByText('RLAB_SECRET_DEMO_API_KEY').first()).toBeVisible();
    await expect(page.getByText('shared-demo-key-123456')).toHaveCount(0);

    await page.getByRole('button', { name: /规则/ }).click();
    await expect(page.getByText(/default/).first()).toBeVisible();
  });

  test('create and edit a scenario through the UI', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: /场景/ }).click();
    await page.getByTestId('new-scenario').click();

    await page.getByTestId('f-name').fill('e2e: created scenario');
    await page.getByTestId('f-path').fill('/users/{id}');
    // path param row: fill the two inputs of the first kv row after adding
    for (const label of ['路径参数', '查询参数']) {
      const section = page.locator('h3', { hasText: label });
      await section.scrollIntoViewIfNeeded();
    }
    // add path parameter id=42: type into the path-params editor and press Enter
    const kvrows = page.getByTestId('scenario-form').locator('.kvrow');
    await kvrows.nth(0).locator('input').nth(0).fill('id');
    await kvrows.nth(0).locator('input').nth(0).press('Enter');
    // after Enter the row persists with an empty value input
    await page.getByTestId('scenario-form').locator('.kvrow').nth(0).locator('input').nth(1).fill('42');

    await page.getByTestId('save-scenario').click();
    await expect(page.getByText('e2e: created scenario')).toBeVisible();

    // edit it: disable
    const row = page.locator('tr', { hasText: 'e2e: created scenario' }).first();
    await row.getByRole('button', { name: '编辑' }).click();
    await page.getByText('启用（运行时包含此场景）').locator('input').uncheck();
    await page.getByTestId('save-scenario').click();
    await expect(page.getByText('e2e: created scenario')).toBeVisible();
  });
  test('run against fixtures yields a set of matches and a set of diffs', async ({ page }) => {
    // First trigger a run through the UI.
    await page.goto('/');
    await page.getByTestId('start-run').click();

    await expect(page.getByTestId('run-progress')).toBeVisible();
    await expect(page.getByText('Completed').first()).toBeVisible({ timeout: 60_000 });

    // Both outcome kinds are present.
    await expect(page.getByTestId('result-Match').first()).toBeVisible();
    await expect(page.getByTestId('result-Diff').first()).toBeVisible();

    // Expand the status-code scenario (a later row) and verify concrete codes are shown.
    const statusRow = page.getByRole('row', { name: /candidate returns 201/ });
    await statusRow.click();
    const detail = page.getByTestId('diff-detail').last();
    await expect(detail).toContainText('200');
    await expect(detail).toContainText('201');
    await statusRow.click(); // collapse

    // Expand the body-diff scenario and verify its field-level diff is shown.
    const bodyDiffRow = page.getByRole('row', { name: /user email field changed/ });
    await bodyDiffRow.click();
    const emailDetail = page.getByTestId('diff-detail').filter({ hasText: 'carol' });
    await expect(emailDetail).toContainText('$.email');
    await expect(emailDetail).toContainText('carol@example.com');
    await expect(emailDetail).toContainText('carol.chen@newmail.example');
  });

  test('secret value never reaches the UI; responses show the mask', async ({ page }) => {
    const run = await postJson('/api/runs', {});
    await waitForRun(page, run.id);
    const results = await getJson<Array<{ scenarioName: string; baseline: { bodyText: string } }>>(
      `/api/runs/${run.id}/results`
    );
    const echo = results.find((r) => r.scenarioName.includes('secret header'))!;
    expect(echo).toBeTruthy();
    expect(echo.baseline.bodyText).not.toContain('shared-demo-key-123456');
    expect(echo.baseline.bodyText).toContain('***');

    // And the UI itself doesn't leak it anywhere while browsing.
    await page.goto('/');
    await page.getByTestId('result-Match').first().click();
    await expect(page.locator('body')).not.toContainText('shared-demo-key-123456');
  });

  test('rule versioning: new version is snapshotted by the run that used it', async ({ page }) => {
    const rulesBefore = await getJson<Array<{ version: number }>>('/api/rules');
    const nextVersion = Math.max(...rulesBefore.map((r) => r.version)) + 1;

    await page.goto('/');
    await page.getByRole('button', { name: /规则/ }).click();
    // Derive a new version FROM the current default so all its settings carry over.
    await page.getByRole('button', { name: '出新版本' }).first().click();
    // The form is prefilled with the default's settings; save unchanged.
    await expect(page.getByTestId('rule-name')).toHaveValue('default');
    await page.getByTestId('save-rule').click();
    const savedText = new RegExp(`已保存为新版本 v${nextVersion}`);
    await expect(page.getByText(savedText)).toBeVisible();

    // The history table still keeps an older version.
    await expect(page.getByRole('cell', { name: `v${nextVersion - 1}`, exact: true })).toBeVisible();

    // Start a run and verify it records the exact version it used.
    await page.getByRole('button', { name: /运行/ }).click();
    await page.getByTestId('start-run').click();
    await expect(page.getByText('Completed').first()).toBeVisible({ timeout: 60_000 });
    await expect(page.getByTestId('run-progress')).toContainText(`default v${nextVersion}`);

    // Historical runs keep their version even after yet another version is published.
    const vNextRunsBefore = (
      await getJson<Array<{ ruleSetVersion: number }>>('/api/runs')
    ).filter((r) => r.ruleSetVersion === nextVersion).length;
    expect(vNextRunsBefore).toBeGreaterThan(0);
  });

  test('unreachable candidate is reported as network failure, not an empty response', async ({ page }) => {
    const run = await postJson('/api/runs', {
      baselineBaseUrl: 'http://baseline:8080',
      candidateBaseUrl: 'http://127.0.0.1:1', // closed port -> connect refused
      options: { concurrency: 4, timeoutMs: 5000 }
    });
    await waitForRun(page, run.id);
    const summary = await getJson<{ networkFailureCount: number; diffCount: number }>(
      `/api/runs/${run.id}`
    );
    expect(summary.networkFailureCount).toBeGreaterThan(0);
    expect(summary.diffCount).toBe(0);

    const results = await getJson<
      Array<{ outcome: string; candidate: { reached: boolean; errorKind: string; statusCode: number | null }; diffs: Array<{ area: string }> }>
    >(`/api/runs/${run.id}/results`);
    for (const r of results) {
      expect(r.candidate.reached).toBe(false);
      expect(r.candidate.statusCode).toBeNull();
      expect(r.diffs.some((d) => d.area === 'Reachability')).toBe(true);
    }

    // The UI labels it distinctly.
    await page.goto('/');
    await expect(page.getByTestId('result-NetworkFailure').first()).toBeVisible({ timeout: 15_000 });
  });

  test('cancel stops new requests and keeps completed results', async ({ page }) => {
    // Deterministic setup: disable every existing scenario, then add one FAST and
    // one SLOW scenario. At concurrency 1, ordered by name, the fast one finishes
    // first; we cancel before the slow one completes and assert it was retained.
    const all = await getJson<Array<any>>('/api/scenarios');
    for (const s of all) {
      await fetch(`${BASE}/api/scenarios/${s.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...s, enabled: false })
      });
    }

    await postJson('/api/scenarios', { name: 'e2e: aaa-fast', method: 'GET', pathTemplate: '/health' });
    await postJson('/api/scenarios', {
      name: 'e2e: zzz-slow',
      method: 'GET',
      pathTemplate: '/slow/30000'
    });

    const run = await postJson('/api/runs', {
      options: { concurrency: 1, timeoutMs: 40_000 }
    });

    // Wait for exactly the fast scenario to be persisted.
    await expect
      .poll(
        async () =>
          (await getJson<Array<unknown>>(`/api/runs/${run.id}/results`)).length,
        { timeout: 30_000, intervals: [300, 500] }
      )
      .toBe(1);

    await fetch(`${BASE}/api/runs/${run.id}/cancel`, { method: 'POST' });

    await expect
      .poll(
        async () => (await getJson<{ status: string }>(`/api/runs/${run.id}`)).status,
        { timeout: 30_000, intervals: [300, 500] }
      )
      .toBe('Cancelled');

    // The already-finished fast result survives cancellation; the slow one never finishes.
    const final = await getJson<{ status: string; completedScenarios: number; totalScenarios: number }>(
      `/api/runs/${run.id}`
    );
    expect(final.completedScenarios).toBe(1);
    expect(final.totalScenarios).toBe(2);
    const results = await getJson<Array<{ scenarioName: string; outcome: string }>>(
      `/api/runs/${run.id}/results`
    );
    expect(results).toHaveLength(1);
    expect(results[0].scenarioName).toBe('e2e: aaa-fast');

    await page.goto('/');
    await expect(page.getByText('Cancelled').first()).toBeVisible();
  });
});
