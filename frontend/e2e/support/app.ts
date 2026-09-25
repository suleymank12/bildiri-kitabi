import AxeBuilder from '@axe-core/playwright';
import { expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFileSync, readdirSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const papersDirectory = resolve(dirname(fileURLToPath(import.meta.url)), '../../../testdata/bildiriler');

/** The ten sample papers that came with the case, in file name order. */
export const paperFiles: string[] = readdirSync(papersDirectory)
  .filter((name) => name.endsWith('.docx'))
  .sort()
  .map((name) => join(papersDirectory, name));

export const isPhone = (page: Page): boolean => (page.viewportSize()?.width ?? 1280) < 1024;

/** Step 1 through the UI: name, ten files, upload; ends on the book page. */
export async function uploadThroughUi(
  page: Page,
  name: string,
  files: string[] = paperFiles,
): Promise<string> {
  await page.goto('/');
  await page.getByLabel('Kitap adı').fill(name);
  await page.getByLabel('Bildiri dosyaları').setInputFiles(files);
  const submit = page.getByRole('button', { name: 'Yükle ve devam et' });
  await expect(submit).toBeEnabled();
  await submit.click();
  await page.waitForURL(/\/kitaplar\/[0-9a-f-]{36}$/);
  return page.url().split('/').at(-1) ?? '';
}

/** Creates a book straight through the API (for tests that start later in the flow). */
export async function createThroughApi(request: APIRequestContext, name: string): Promise<string> {
  const form = new FormData();
  form.append('name', name);
  for (const file of paperFiles) {
    form.append('files', new Blob([readFileSync(file)]), basename(file));
  }

  const response = await request.post('/api/books', { multipart: form });
  expect(response.status()).toBe(201);
  const book = (await response.json()) as { id: string };
  return book.id;
}

export async function generateThroughApi(request: APIRequestContext, id: string): Promise<void> {
  const response = await request.post(`/api/books/${id}/generate`);
  expect(response.status()).toBe(202);
  await expect
    .poll(async () => ((await (await request.get(`/api/books/${id}`)).json()) as { status: string }).status, {
      timeout: 60_000,
    })
    .toBe('Completed');
}

/** No serious or critical WCAG 2.1 A/AA violations on the current screen. */
export async function expectAccessible(page: Page, screen: string): Promise<void> {
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  const serious = results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map(
      (violation) =>
        `${violation.id}: ${violation.help} (${violation.nodes.map((node) => node.target.join(' ')).join(', ')})`,
    );
  expect(serious, `${screen}: erişilebilirlik ihlalleri`).toEqual([]);
}

/** The page number the viewer shows (page box on desktop, "n / total" on phones). */
export async function currentPage(page: Page): Promise<number> {
  if (isPhone(page)) {
    const text = await page.getByTestId('page-indicator').innerText();
    return Number.parseInt(text, 10);
  }

  return Number.parseInt(await page.getByLabel('Sayfa numarası').inputValue(), 10);
}

/** Opens a paper from the viewer's table of contents (side panel on desktop, bottom sheet on phones). */
export async function openFromContents(page: Page, title: RegExp): Promise<void> {
  if (isPhone(page)) {
    await page.getByRole('button', { name: 'İçindekiler' }).click();
    await page.getByRole('dialog', { name: 'İçindekiler' }).getByRole('button', { name: title }).click();
  } else {
    await page.getByRole('navigation', { name: 'Bildiriler' }).getByRole('button', { name: title }).click();
  }
}
