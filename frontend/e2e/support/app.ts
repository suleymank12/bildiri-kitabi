import AxeBuilder from '@axe-core/playwright';
import { expect, type APIRequestContext, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { basename } from 'node:path';
import { listPaperFiles } from './papers';

/** The ten sample papers that came with the case, in file name order (checked in playwright.config.ts). */
export const paperFiles: string[] = listPaperFiles();

export const isPhone = (page: Page): boolean => (page.viewportSize()?.width ?? 1280) < 1024;

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

/**
 * A double tap as a touch screen reports it: every touch carries the time it happened, and the two taps are 80 ms
 * apart. `page.touchscreen.tap` stamps a tap when it is sent and sends the next one only after the page has handled
 * it, so while the viewer is still drawing a page the two taps drift apart past the double-tap limit.
 */
export async function doubleTap(page: Page, x: number, y: number): Promise<void> {
  const session = await page.context().newCDPSession(page);
  const start = Date.now() / 1000;
  try {
    for (const offset of [0, 0.08]) {
      await session.send('Input.dispatchTouchEvent', {
        type: 'touchStart',
        touchPoints: [{ x, y }],
        timestamp: start + offset,
      });
      await session.send('Input.dispatchTouchEvent', {
        type: 'touchEnd',
        touchPoints: [],
        timestamp: start + offset + 0.02,
      });
    }
  } finally {
    await session.detach();
  }
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
