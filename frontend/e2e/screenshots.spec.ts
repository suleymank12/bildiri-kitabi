import { expect, test, type Page } from '@playwright/test';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createThroughApi, generateThroughApi, paperFiles } from './support/app';

// README images (`npm run screenshots`), written to docs/screenshots/. The browser clock is fixed and the
// book's timestamps are pinned in the API responses, so every run produces the same pictures.
const output = resolve(dirname(fileURLToPath(import.meta.url)), '../../docs/screenshots');
const BOOK_NAME = 'Örnek Bilim Kongresi 2026';
const NOW = new Date('2026-09-29T10:00:00+03:00');
const at = (secondsBefore: number) => new Date(NOW.getTime() - secondsBefore * 1000).toISOString();

test.describe.configure({ mode: 'serial' });
test.use({ locale: 'tr-TR', timezoneId: 'Europe/Istanbul', reducedMotion: 'reduce', colorScheme: 'light' });

let completedId = '';

async function prepare(page: Page): Promise<void> {
  await page.clock.setFixedTime(NOW);
}

const overrides = new Map<string, Record<string, unknown>>();

/**
 * Pins the timestamps of one book (and applies a state override) in every response of GET /api/books/{id}.
 * One route per book; later calls only swap the override, so an in-flight poll is never handled twice.
 */
async function pinBook(page: Page, id: string, override: Record<string, unknown> = {}): Promise<void> {
  const known = overrides.has(id);
  overrides.set(id, override);
  if (known) {
    return;
  }

  await page.route(`**/api/books/${id}`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.continue();
      return;
    }

    const response = await route.fetch();
    const book = (await response.json()) as Record<string, unknown>;
    await route.fulfill({
      response,
      json: {
        ...book,
        createdAt: at(600),
        processingStartedAt: book.processingStartedAt ? at(90) : null,
        processingFinishedAt: book.processingFinishedAt ? at(85) : null,
        ...overrides.get(id),
      },
    });
  });
}

async function shoot(page: Page, name: string, fullPage = true): Promise<void> {
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: join(output, name), fullPage, animations: 'disabled', caret: 'hide' });
}

async function fillStepOne(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByLabel('Kitap adı').fill(BOOK_NAME);
  await page.getByLabel('Bildiri dosyaları').setInputFiles(paperFiles);
  await expect(page.getByRole('button', { name: 'Yükle ve devam et' })).toBeEnabled();
  await page.getByLabel('Kitap adı').blur();
}

test.describe('masaüstü', () => {
  test.use({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 });

  test('adımlar, üretim, görüntüleyici ve hata', async ({ page, request }) => {
    await prepare(page);

    await fillStepOne(page);
    await shoot(page, 'masaustu-adim1.png');

    await page.getByRole('button', { name: 'Yükle ve devam et' }).click();
    await page.waitForURL(/\/kitaplar\/[0-9a-f-]{36}$/);
    completedId = page.url().split('/').at(-1) ?? '';
    await pinBook(page, completedId);
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Sıra ve kontrol' })).toBeVisible();
    await shoot(page, 'masaustu-adim2.png');

    // The real generation takes about a second; the waiting screen is captured in a pinned in-progress state.
    await pinBook(page, completedId, {
      status: 'Processing',
      stage: 'Rendering',
      progressPercent: 45,
      processingStartedAt: at(8),
    });
    await page.reload();
    await expect(page.getByRole('list', { name: 'Aşamalar' })).toBeVisible();
    await shoot(page, 'masaustu-uretim.png');

    await generateThroughApi(request, completedId);
    await pinBook(page, completedId);
    await page.goto(`/kitaplar/${completedId}?sayfa=3`);
    await expect(page.locator('[data-page-slot="3"] .textLayer')).toContainText('KENTSEL TARIMDA');
    await expect(page.locator('[data-page-slot="2"] canvas')).toBeVisible();
    await shoot(page, 'masaustu-goruntuleyici.png');

    // A large screen, as the viewer opens by default: facing pages fitted to the width, the cover centred.
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto(`/kitaplar/${completedId}`);
    await expect(page.locator('[data-page-slot="1"] canvas')).toBeVisible();
    await shoot(page, 'masaustu-goruntuleyici-1920.png', false);
    await page.setViewportSize({ width: 1280, height: 800 });

    await pinBook(page, completedId, {
      status: 'Failed',
      stage: 'Rendering',
      pageCount: null,
      pdfUrl: null,
      pdfSizeBytes: null,
      error: {
        code: 'UNSUPPORTED_CHARACTER',
        message: "3. sıradaki bildiride PDF yazı tipinde bulunmayan '𝒜' karakteri var.",
      },
    });
    await page.goto(`/kitaplar/${completedId}`);
    await expect(page.getByRole('alert')).toContainText('Kitap oluşturulamadı');
    await shoot(page, 'masaustu-hata.png');
  });
});

test.afterEach(() => {
  overrides.clear();
});

test.describe('mobil', () => {
  test.use({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });

  test('adımlar ve görüntüleyici', async ({ page, request }) => {
    await prepare(page);

    await fillStepOne(page);
    await shoot(page, 'mobil-adim1.png');

    const uploadedId = await createThroughApi(request, BOOK_NAME);
    await pinBook(page, uploadedId);
    await page.goto(`/kitaplar/${uploadedId}`);
    await expect(page.getByRole('heading', { name: 'Sıra ve kontrol' })).toBeVisible();
    await shoot(page, 'mobil-adim2.png');

    expect(completedId).not.toBe('');
    await pinBook(page, completedId);
    await page.goto(`/kitaplar/${completedId}?sayfa=3`);
    await expect(page.locator('[data-page-slot="3"] .textLayer')).toContainText('KENTSEL TARIMDA');
    await shoot(page, 'mobil-goruntuleyici.png', false);

    // 200 %: a double tap on the page.
    const slot = page.locator('[data-page-slot="3"]');
    const box = await slot.boundingBox();
    expect(box).not.toBeNull();
    const x = (box?.x ?? 0) + (box?.width ?? 0) * 0.35;
    const y = (box?.y ?? 0) + (box?.height ?? 0) * 0.3;
    await page.touchscreen.tap(x, y);
    await page.touchscreen.tap(x, y);
    await expect.poll(async () => (await slot.boundingBox())?.width ?? 0).toBeGreaterThan(1500);
    await expect(slot.locator('canvas')).toBeVisible();
    await shoot(page, 'mobil-goruntuleyici-yakin.png', false);
  });
});
