import { expect, test } from '@playwright/test';
import { createHash, randomBytes } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

test.describe('upload -> list -> download', () => {
  let tempDir: string;
  let sourceFilePath: string;
  let sourceFileName: string;
  let sourceBytes: Buffer;

  test.beforeAll(() => {
    tempDir = mkdtempSync(path.join(tmpdir(), 'e2e-upload-'));
    sourceFileName = `e2e-${Date.now()}.txt`;
    sourceFilePath = path.join(tempDir, sourceFileName);
    sourceBytes = Buffer.concat([Buffer.from('e2e-test-file\n'), randomBytes(2048)]);
    writeFileSync(sourceFilePath, sourceBytes);
  });

  test.afterAll(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });

  test('logs in, uploads a file, sees it listed, and downloads identical bytes', async ({ page }) => {
    await page.goto('/login');

    await page.getByRole('button', { name: /sign in as user/i }).click();
    await expect(page).toHaveURL(/\/files$/);

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(sourceFilePath);

    const uploadRow = page.locator('.upload-item', { hasText: sourceFileName });
    await expect(uploadRow).toBeVisible();
    await uploadRow.getByRole('button', { name: 'Upload' }).click();
    await expect(uploadRow.getByText('Uploaded')).toBeVisible({ timeout: 15_000 });

    const listLink = page.locator('.file-row__name', { hasText: sourceFileName });
    await expect(listLink).toBeVisible({ timeout: 10_000 });

    const [download] = await Promise.all([
      page.waitForEvent('download'),
      (async () => {
        const row = page.locator('.file-row', { hasText: sourceFileName });
        await row.getByRole('button', { name: 'Download' }).click();
      })(),
    ]);

    const downloadedPath = path.join(tempDir, 'downloaded-' + sourceFileName);
    await download.saveAs(downloadedPath);

    const downloadedBytes = readFileSync(downloadedPath);
    expect(createHash('sha256').update(downloadedBytes).digest('hex')).toBe(
      createHash('sha256').update(sourceBytes).digest('hex'),
    );
    expect(downloadedBytes.length).toBe(sourceBytes.length);
  });
});
