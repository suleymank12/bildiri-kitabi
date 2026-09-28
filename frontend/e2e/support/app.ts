import AxeBuilder from '@axe-core/playwright';
import { expect, type APIRequestContext, type Locator, type Page } from '@playwright/test';
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
  const book = (await response.json()) as { uid: string };
  return book.uid;
}

export async function generateThroughApi(request: APIRequestContext, uid: string): Promise<void> {
  const response = await request.post(`/api/books/${uid}/generate`);
  expect(response.status()).toBe(202);
  await expect
    .poll(
      async () => ((await (await request.get(`/api/books/${uid}`)).json()) as { status: string }).status,
      {
        timeout: 60_000,
      },
    )
    .toBe('Completed');
}

/** Opens Kitaplarım and its "Yeni kitap" modal; returns the modal. */
export async function openNewBookDialog(page: Page): Promise<Locator> {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1, name: 'Kitaplarım' })).toBeVisible();
  // An empty list has a second "Yeni kitap" button in its empty state; both open the same modal.
  await page.getByRole('button', { name: 'Yeni kitap' }).first().click();
  const dialog = page.getByRole('dialog', { name: 'Yeni kitap' });
  await expect(dialog).toBeVisible();
  await expect(dialog.getByLabel('Kitap adı')).toBeFocused();
  return dialog;
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

/**
 * Holds the server's answer to the next upload (POST /api/books) until `release` is called, then passes it on
 * unchanged. The request itself goes to the real API untouched: with `page.route` Chromium reports no upload
 * progress, so the page would never reach its "checking the files" stage while the request waits.
 */
export async function holdUploadResponse(page: Page): Promise<{ release: () => Promise<void> }> {
  const session = await page.context().newCDPSession(page);
  const paused = new Promise<string>((resolve) => {
    session.on('Fetch.requestPaused', (event) => {
      if (event.request.method === 'POST') {
        resolve(event.requestId);
      } else {
        void session.send('Fetch.continueResponse', { requestId: event.requestId });
      }
    });
  });
  await session.send('Fetch.enable', { patterns: [{ urlPattern: '*/api/books', requestStage: 'Response' }] });

  return {
    release: async () => {
      await session.send('Fetch.continueResponse', { requestId: await paused });
      await session.send('Fetch.disable');
      await session.detach();
    },
  };
}

/** Waits until the CSS transitions and animations of an element and its children have finished. */
export async function animationsFinished(locator: Locator): Promise<void> {
  await locator.evaluate((element) =>
    Promise.all(element.getAnimations({ subtree: true }).map((animation) => animation.finished)),
  );
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
