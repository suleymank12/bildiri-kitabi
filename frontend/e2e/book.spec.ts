import { expect, test } from '@playwright/test';
import { statSync } from 'node:fs';
import {
  createThroughApi,
  currentPage,
  expectAccessible,
  generateThroughApi,
  openFromContents,
  paperFiles,
  uploadThroughUi,
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

  await uploadThroughUi(page, 'Örnek Bilim Kongresi 2026');

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
  await expect(page.getByText('Kitap hazır', { exact: true })).toBeVisible({ timeout: 60_000 });
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

  await row.getByRole('button', { name: `${name} için işlemler` }).click();
  await page.getByRole('menuitem', { name: 'Sil' }).click();
  const dialog = page.getByRole('dialog', { name: 'Kitabı sil' });
  await expect(dialog).toContainText(name);
  await dialog.getByRole('button', { name: 'Sil' }).click();

  await expect(page.getByRole('link', { name })).toHaveCount(0);
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
      const footer = document.querySelector('footer')?.getBoundingClientRect().bottom ?? 0;
      return {
        overflow: document.documentElement.scrollWidth - width,
        belowFooter: document.documentElement.scrollHeight - (footer + window.scrollY),
        small,
      };
    });

    expect(layout.overflow, `${path}: yatay taşma`).toBeLessThanOrEqual(0);
    expect(layout.belowFooter, `${path}: altbilginin altında boşluk`).toBeLessThanOrEqual(1);
    expect(layout.small, `${path}: 44 px'ten küçük dokunma hedefleri`).toEqual([]);
  }
});
