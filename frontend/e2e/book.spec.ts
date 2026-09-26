import { expect, test } from '@playwright/test';
import { statSync } from 'node:fs';
import {
  createThroughApi,
  currentPage,
  expectAccessible,
  generateThroughApi,
  isPhone,
  openFromContents,
  paperFiles,
} from './support/app';

// Books created here stay in the database of a long-running setup (npm run test:e2e:docker); a per-run token
// keeps their names unique across runs.
const run = Date.now().toString(36);

test('mutlu yol: yükleme, sıralama, oluşturma, görüntüleyicide gezinme ve indirme', async ({ page }) => {
  // Behind nginx (npm run test:e2e:docker) the pages carry a Content-Security-Policy; the viewer's pdf.js
  // worker, fonts and decoders must work under it.
  const cspViolations: string[] = [];
  page.on('console', (message) => {
    if (message.text().includes('Content Security Policy')) cspViolations.push(message.text());
  });

  await page.goto('/');
  await expectAccessible(page, 'Adım 1');

  // Step 1. The server's answer is held back a little, so the second stage of the upload (the server checking
  // the files) is on screen long enough to be seen.
  await page.route('**/api/books', async (route) => {
    if (route.request().method() === 'POST') {
      await new Promise((resolve) => setTimeout(resolve, 1500));
    }

    await route.continue();
  });
  await page.getByLabel('Kitap adı').fill('Örnek Bilim Kongresi 2026');
  await page.getByLabel('Bildiri dosyaları').setInputFiles(paperFiles);
  const submit = page.getByRole('button', { name: 'Yükle ve devam et' });
  await expect(submit).toBeEnabled();
  await submit.scrollIntoViewIfNeeded();
  await submit.click();
  await expect(page.getByText('Dosyalar kontrol ediliyor ve başlıklar tespit ediliyor…')).toBeVisible();
  await page.waitForURL(/\/kitaplar\/[0-9a-f-]{36}$/);
  await page.unroute('**/api/books');

  // The new page starts at the top with the focus on its heading.
  await expect(page.getByRole('heading', { level: 1, name: 'Örnek Bilim Kongresi 2026' })).toBeFocused();
  expect(await page.evaluate(() => window.scrollY)).toBe(0);

  // Step 2: detected titles, then move the first paper one place down.
  await expect(page.getByRole('heading', { name: 'Sıra ve kontrol' })).toBeVisible();
  await expect(
    page.getByText('KENTSEL TARIMDA AKILLI SULAMA SİSTEMLERİNİN SU TÜKETİMİNE ETKİSİ'),
  ).toBeVisible();
  await expectAccessible(page, 'Adım 2');
  const saved = page.waitForResponse(
    (r) => r.url().includes('/paper-order') && r.request().method() === 'PUT',
  );
  await page.getByRole('button', { name: '01_Akilli_Sulama.docx dosyasını aşağı taşı' }).click();
  expect((await saved).status()).toBe(200);
  await expect(page.getByText('Sıra, yükleme sırasından farklı.')).toBeVisible();

  // Generation: the stage list appears, then the viewer.
  await page.getByRole('button', { name: 'Kitabı Oluştur' }).click();
  await expect(page.getByRole('list', { name: 'Aşamalar' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Temizlenen iletişim bilgileri' })).toBeVisible({
    timeout: 60_000,
  });
  await expect(page.locator('.react-pdf__Page canvas').first()).toBeVisible();
  await expectAccessible(page, 'Görüntüleyici');

  // The fifth paper after the move is still 05_Uzaktan_Egitim; it starts on page 11.
  await openFromContents(page, /UZAKTAN EĞİTİMDE ETKİLEŞİMLİ/);
  await expect.poll(() => currentPage(page)).toBe(11);
  await expect(page.locator('[data-page-slot="11"] .textLayer')).toContainText('UZAKTAN EĞİTİMDE');
  expect(new URL(page.url()).searchParams.get('sayfa')).toBe('11');

  // Download.
  const downloading = page.waitForEvent('download');
  await page.getByRole('link', { name: 'İndir' }).click();
  const download = await downloading;
  expect(download.suggestedFilename()).toMatch(/\.pdf$/);
  expect(statSync(await download.path()).size).toBeGreaterThan(0);

  expect(cspViolations).toEqual([]);
});

test('istemci doğrulaması: eksik dosya, yanlış tür ve mükerrer dosya', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Kitap adı').fill('Doğrulama Denemesi');
  const input = page.getByLabel('Bildiri dosyaları');
  const submit = page.getByRole('button', { name: 'Yükle ve devam et' });

  await input.setInputFiles(paperFiles.slice(0, 9));
  await expect(page.getByText(/10 dosyadan 9'u seçildi/)).toBeVisible();
  await expect(submit).toBeDisabled();

  await input.setInputFiles({
    name: 'makale.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.7'),
  });
  const pdfRow = page
    .getByRole('list', { name: 'Seçilen dosyalar' })
    .getByRole('listitem')
    .filter({ hasText: 'makale.pdf' });
  await expect(pdfRow.getByText('Yalnızca .docx uzantılı Word belgeleri yüklenebilir.')).toBeVisible();

  await input.setInputFiles(paperFiles[0] ?? '');
  await expect(page.getByText('01_Akilli_Sulama.docx ile aynı içeriğe sahip.')).toBeVisible();
  await expect(submit).toBeDisabled();
});

test('sunucu hatası: kitap adındaki emoji ad alanında gösterilir', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Kitap adı').fill('Kongre 2026 😀');
  await page.getByLabel('Bildiri dosyaları').setInputFiles(paperFiles);
  await page.getByRole('button', { name: 'Yükle ve devam et' }).click();

  await expect(page.getByLabel('Kitap adı')).toHaveAccessibleDescription(
    /Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın\./,
  );
  await expect(page).toHaveURL(/\/$/);
});

test('üretim hatası: hata ekranı, tekrar dene ve sırayı düzenle', async ({ page, request }) => {
  const id = await createThroughApi(request, 'Hata Ekranı Denemesi');
  await page.route(`**/api/books/${id}`, async (route) => {
    const response = await route.fetch();
    const book = (await response.json()) as Record<string, unknown>;
    await route.fulfill({
      response,
      json: {
        ...book,
        status: 'Failed',
        stage: 'Rendering',
        error: { code: 'RENDER_FAILED', message: 'PDF dizgisi oluşturulamadı.' },
      },
    });
  });

  await page.goto(`/kitaplar/${id}`);

  const alert = page.getByRole('alert');
  await expect(alert).toContainText('Kitap oluşturulamadı');
  await expect(alert).toContainText('PDF dizgisi oluşturulamadı.');
  await expect(alert).toContainText('RENDER_FAILED');
  await expect(alert.getByRole('button', { name: 'Tekrar dene' })).toBeVisible();
  await expect(alert.getByRole('button', { name: 'Sırayı düzenle' })).toBeVisible();
  await expectAccessible(page, 'Hata ekranı');

  await alert.getByRole('button', { name: 'Sırayı düzenle' }).click();
  await expect(page.getByRole('heading', { name: 'Sıra ve kontrol' })).toBeVisible();
});

test('bekleme ekranı erişilebilir', async ({ page, request }) => {
  const id = await createThroughApi(request, 'Bekleme Ekranı Denemesi');
  await page.route(`**/api/books/${id}`, async (route) => {
    const response = await route.fetch();
    const book = (await response.json()) as Record<string, unknown>;
    await route.fulfill({
      response,
      json: {
        ...book,
        status: 'Processing',
        stage: 'Rendering',
        progressPercent: 45,
        processingStartedAt: new Date().toISOString(),
      },
    });
  });

  await page.goto(`/kitaplar/${id}`);
  await expect(page.getByRole('list', { name: 'Aşamalar' })).toBeVisible();
  await expectAccessible(page, 'Bekleme ekranı');
});

test('Kitaplarım: kitap listede görünür ve onayla silinir', async ({ page, request }, testInfo) => {
  const name = `Silinecek Kitap ${testInfo.project.name} ${run}`;
  const id = await createThroughApi(request, name);
  await generateThroughApi(request, id);

  await page.goto('/kitaplar');
  const row = page.getByRole('listitem').filter({ has: page.getByRole('link', { name }) });
  await expect(row).toBeVisible();
  await expect(row.getByText('Hazır')).toBeVisible();
  await expectAccessible(page, 'Kitaplarım');

  await row.getByRole('button', { name: `${name} kitabını sil` }).click();
  const dialog = page.getByRole('dialog', { name: 'Kitabı sil' });
  await expect(dialog).toContainText(name);
  await dialog.getByRole('button', { name: 'Sil' }).click();

  await expect(page.getByRole('link', { name })).toHaveCount(0);
});

test('görüntüleyici: varsayılan açılış, ortalanmış kapak, yakınlaştırma ve çift dokunma', async ({
  page,
  request,
}, testInfo) => {
  const id = await createThroughApi(request, `Görüntüleyici Denemesi ${testInfo.project.name} ${run}`);
  await generateThroughApi(request, id);
  const phone = isPhone(page);
  if (!phone) {
    await page.setViewportSize({ width: 1920, height: 1080 });
  }

  await page.goto(`/kitaplar/${id}`);
  const area = page.getByTestId('page-area');
  const horizontalOverflow = () => area.evaluate((element) => element.scrollWidth - element.clientWidth);
  const cover = page.locator('[data-page-slot="1"]');
  await expect(cover.locator('canvas')).toBeVisible();
  expect(await horizontalOverflow()).toBeLessThanOrEqual(0);

  if (!phone) {
    // Default: facing pages fitted to the width; the cover stands alone in the middle.
    await expect(page.getByRole('button', { name: 'Genişliğe sığdır' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await expect(page.getByRole('button', { name: 'Çift sayfa' })).toHaveAttribute('aria-pressed', 'true');
    const areaBox = await area.boundingBox();
    const coverBox = await cover.boundingBox();
    const middle = (box: { x: number; width: number } | null) => (box ? box.x + box.width / 2 : 0);
    // The scroll bar gutter on the right makes the area's middle a few pixels off the visible middle.
    expect(Math.abs(middle(coverBox) - middle(areaBox))).toBeLessThan(10);

    // Pages 2 and 3: how large the body text is on a 1920 × 1080 screen.
    await page.getByRole('button', { name: 'Sonraki sayfa' }).click();
    await expect(page.locator('[data-page-slot="3"] .textLayer')).toContainText('ÖZET');
    const body = page.locator('[data-page-slot="3"] .textLayer span');
    const line = await body.evaluateAll((spans) => {
      const heights = spans
        .filter((span) => span.textContent.trim().length > 30)
        .map((span) => span.getBoundingClientRect().height)
        .sort((a, b) => a - b);
      return heights[Math.floor(heights.length / 2)] ?? 0;
    });
    testInfo.annotations.push({ type: 'gövde metni (1920 × 1080)', description: `${line.toFixed(1)} px` });
    expect(line).toBeGreaterThanOrEqual(12);

    // Zooming in keeps the page and starts from the middle of the pages, not their left edge.
    await page.getByRole('button', { name: 'Yakınlaştır' }).click();
    await expect.poll(horizontalOverflow).toBeGreaterThan(0);
    expect(await currentPage(page)).toBe(2);
    const { left, overflow } = await area.evaluate((element) => ({
      left: element.scrollLeft,
      overflow: element.scrollWidth - element.clientWidth,
    }));
    expect(Math.abs(left - overflow / 2)).toBeLessThan(overflow * 0.1 + 2);

    // A zoom typed into the percentage box, on the same page.
    const zoomBox = page.getByLabel('Yakınlaştırma yüzdesi');
    await zoomBox.click();
    await zoomBox.pressSequentially('150');
    await zoomBox.press('Enter');
    await expect(zoomBox).toHaveValue('%150');
    await expect(page.getByRole('button', { name: 'Genişliğe sığdır' })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
    expect(await currentPage(page)).toBe(2);

    // "Sayfaya sığdır": both pages completely visible.
    await page.getByRole('button', { name: 'Sayfaya sığdır' }).click();
    await expect.poll(horizontalOverflow).toBeLessThanOrEqual(0);
    const pageBox = await page.locator('[data-page-slot="3"]').boundingBox();
    const shown = await area.boundingBox();
    expect(pageBox && shown && pageBox.y + pageBox.height <= shown.y + shown.height + 1).toBe(true);
  } else {
    // Phones: a double tap zooms to 200 % around the tap, a second one fits the width again. Page 3 is body
    // text (page 2, the table of contents, is all links).
    await page.goto(`/kitaplar/${id}?sayfa=3`);
    const slot = page.locator('[data-page-slot="3"]');
    await expect(slot.locator('canvas')).toBeVisible();
    const fitted = (await slot.boundingBox())?.width ?? 0;
    const box = await slot.boundingBox();
    const x = (box?.x ?? 0) + (box?.width ?? 0) * 0.3;
    const y = (box?.y ?? 0) + Math.min(200, (box?.height ?? 0) / 2);
    await page.touchscreen.tap(x, y);
    await page.touchscreen.tap(x, y);
    await expect.poll(async () => (await slot.boundingBox())?.width ?? 0).toBeGreaterThan(fitted * 1.8);
    expect(await horizontalOverflow()).toBeGreaterThan(0);
    expect(await currentPage(page)).toBe(3);

    await page.getByRole('button', { name: 'Sonraki sayfa' }).click();
    await expect.poll(() => currentPage(page)).toBe(4);

    // The zoomed page fills the screen; tapping twice in its middle fits it to the width again.
    await page.touchscreen.tap(195, 420);
    await page.touchscreen.tap(195, 420);
    await expect.poll(horizontalOverflow).toBeLessThanOrEqual(0);
    await expect(page.getByRole('button', { name: 'Uzaklaştır' })).toBeDisabled();
  }
});

test('düzen: yatay kaydırma yok ve dokunma hedefleri en az 44 px', async ({ page, request }, testInfo) => {
  const suffix = `${testInfo.project.name} ${run}`;
  const id = await createThroughApi(
    request,
    `Düzen Denemesi Uzun Bir Kitap Adı ile Satır Kırılımı Kontrolü ${suffix}`,
  );
  const finished = await createThroughApi(request, `Düzen Denemesi Hazır Kitap ${suffix}`);
  await generateThroughApi(request, finished);

  const screens: [string, string][] = [
    ['/', 'Yeni kitap'],
    ['/kitaplar', 'Kitaplarım'],
    [`/kitaplar/${id}`, `Düzen Denemesi Uzun Bir Kitap Adı ile Satır Kırılımı Kontrolü ${suffix}`],
    [`/kitaplar/${finished}`, `Düzen Denemesi Hazır Kitap ${suffix}`],
    ['/olmayan-sayfa', 'Sayfa bulunamadı'],
  ];
  for (const [path, heading] of screens) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1, name: heading })).toBeVisible();
    if (path === '/kitaplar') {
      await expect(page.getByRole('link', { name: `Düzen Denemesi Hazır Kitap ${suffix}` })).toBeVisible();
    } else if (path.endsWith(finished)) {
      await expect(page.locator('.react-pdf__Page canvas').first()).toBeVisible();
    }

    const layout = await page.evaluate(() => {
      const width = document.documentElement.clientWidth;
      const small = Array.from(
        document.querySelectorAll<HTMLElement>('button, a[href], input, [role="menuitem"]'),
      )
        .filter((element) => {
          const box = element.getBoundingClientRect();
          const style = getComputedStyle(element);
          const hidden =
            box.width === 0 ||
            style.visibility === 'hidden' ||
            element.closest('.sr-only, [aria-hidden="true"], .annotationLayer');
          return !hidden && (box.height < 44 || box.width < 44);
        })
        .map(
          (element) =>
            `${element.tagName} "${(element.getAttribute('aria-label') ?? element.textContent).trim().slice(0, 30)}"`,
        );
      const main = document.querySelector('main')?.getBoundingClientRect().bottom ?? 0;
      return {
        overflow: document.documentElement.scrollWidth - width,
        belowMain: document.documentElement.scrollHeight - (main + window.scrollY),
        small,
      };
    });

    expect(layout.overflow, `${path}: yatay taşma`).toBeLessThanOrEqual(0);
    expect(layout.belowMain, `${path}: içeriğin altında boşluk`).toBeLessThanOrEqual(1);
    expect(layout.small, `${path}: 44 px'ten küçük dokunma hedefleri`).toEqual([]);
  }
});
