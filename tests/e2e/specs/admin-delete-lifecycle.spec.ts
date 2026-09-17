import { expect, test } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

test.describe('admin soft-delete -> reload -> find -> hard-delete', () => {
  let tempDir: string;
  let filePath: string;
  let fileName: string;

  test.beforeAll(() => {
    tempDir = mkdtempSync(path.join(tmpdir(), 'e2e-admin-delete-'));
    fileName = `admin-delete-${Date.now()}.txt`;
    filePath = path.join(tempDir, fileName);
    writeFileSync(filePath, Buffer.concat([Buffer.from('admin-delete-lifecycle\n'), randomBytes(256)]));
  });

  test.afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  test('soft-deleted file is retrievable from the server after reload and can then be hard-deleted', async ({ page }) => {
    page.on('dialog', (dialog) => dialog.accept());

    await page.goto('/login');
    await page.getByRole('button', { name: /sign in as admin/i }).click();
    await expect(page).toHaveURL(/\/files$/);

    await page.locator('input[type="file"]').setInputFiles(filePath);
    const uploadRow = page.locator('.upload-item', { hasText: fileName });
    await uploadRow.getByRole('button', { name: 'Upload' }).click();
    await expect(uploadRow.getByText('Uploaded')).toBeVisible({ timeout: 15_000 });

    const listRow = page.locator('.file-row', { hasText: fileName });
    await expect(listRow).toBeVisible({ timeout: 10_000 });

    await listRow.getByRole('button', { name: 'Delete' }).click();
    await expect(listRow).toHaveCount(0, { timeout: 10_000 });

    await page.reload();
    await expect(page.locator('.file-row', { hasText: fileName })).toHaveCount(0, { timeout: 10_000 });

    const deletedRow = page.locator('.deleted-row', { hasText: fileName });
    await expect(deletedRow).toBeVisible({ timeout: 10_000 });

    await deletedRow.getByRole('button', { name: 'Hard delete' }).click();
    await expect(deletedRow).toHaveCount(0, { timeout: 10_000 });

    await page.reload();
    await expect(page.locator('.file-row', { hasText: fileName })).toHaveCount(0);
    await expect(page.locator('.deleted-row', { hasText: fileName })).toHaveCount(0);
  });

  test('an ordinary user cannot access the admin deleted-files listing', async ({ page }) => {
    await page.goto('/login');
    await page.getByRole('button', { name: /sign in as user/i }).click();
    await expect(page).toHaveURL(/\/files$/);

    await expect(page.locator('.deleted-row').first()).toHaveCount(0);
    await expect(page.getByText('Deleted files')).toHaveCount(0);

    const token = await page.evaluate(() => {
      const raw = sessionStorage.getItem('fileStorage.session');
      return raw ? (JSON.parse(raw) as { accessToken: string }).accessToken : null;
    });
    expect(token).not.toBeNull();

    const response = await page.request.get('/api/files/deleted', {
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(response.status()).toBe(403);
  });
});
