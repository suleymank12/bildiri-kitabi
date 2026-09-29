import { expect, test } from '@playwright/test';
import { readFileSync, statSync } from 'node:fs';
import { basename } from 'node:path';
import {
  animationsFinished,
  createThroughApi,
  currentPage,
  doubleTap,
  expectAccessible,
  generateThroughApi,
  holdUploadResponse,
  isPhone,
  openFromContents,
  openNewBookDialog,
  paperFiles,
} from './support/app';
import { emptyWordDocument, excelWorkbook, powerPointPresentation } from './support/packages';

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

  // Step 1, in the "Yeni kitap" modal over Kitaplarım.
  const dialog = await openNewBookDialog(page);
  await expectAccessible(page, 'Yeni kitap modalı');
  await dialog.getByLabel('Kitap adı').fill('Örnek Bilim Kongresi 2026');
  await dialog.getByLabel('Bildiri dosyaları').setInputFiles(paperFiles);
  const submit = dialog.getByRole('button', { name: 'Yükle ve devam et' });
  await expect(submit).toBeEnabled();

  // The papers arrive in name order: "Ada göre sırala" explains that in a tooltip (hover, or a tap on phones).
  const sort = page.getByRole('button', { name: 'Ada göre sırala' });
  if (isPhone(page)) {
    // Playwright does not tap an aria-disabled element on its own; a person can.
    await sort.tap({ force: true });
  } else {
    await sort.hover();
  }
  const tooltip = page.getByRole('tooltip');
  await expect(tooltip).toHaveText('Dosyalar zaten ada göre sıralı');
  // The tooltip fades in; contrast is measured once it is fully opaque.
  await animationsFinished(tooltip);
  await expectAccessible(page, 'Yeni kitap modalı, ipucu açık');
  await page.keyboard.press('Escape');
  await expect(page.getByRole('tooltip')).toBeHidden();

  // The server's answer is held until the second stage of the upload (the server checking the files) is seen.
  const upload = await holdUploadResponse(page);
  await submit.scrollIntoViewIfNeeded();
  await submit.click();
  await expect(dialog.getByText('Dosyalar kontrol ediliyor ve başlıklar tespit ediliyor…')).toBeVisible();
  // While the upload runs the modal cannot be closed.
  await expect(dialog.getByRole('button', { name: 'Vazgeç' })).toBeDisabled();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeVisible();
  await upload.release();
  await page.waitForURL(/\/kitaplar\/[0-9a-f-]{36}$/);
  await expect(page.getByRole('dialog')).toHaveCount(0);

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
  const dialog = await openNewBookDialog(page);
  await dialog.getByLabel('Kitap adı').fill('Doğrulama Denemesi');
  const input = dialog.getByLabel('Bildiri dosyaları');
  const submit = dialog.getByRole('button', { name: 'Yükle ve devam et' });

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

  // A filled-in modal asks before it forgets everything.
  await dialog.getByRole('button', { name: 'Vazgeç' }).click();
  const question = page.getByRole('dialog', { name: 'Yeni kitap kapatılsın mı?' });
  await expect(question).toContainText('Seçtiğiniz dosyalar ve yazdığınız ad silinecek. Kapatılsın mı?');
  await expectAccessible(page, 'Kapatma onayı');
  await question.getByRole('button', { name: 'Hayır' }).click();
  await expect(dialog.getByLabel('Kitap adı')).toHaveValue('Doğrulama Denemesi');
  await dialog.getByRole('button', { name: 'Vazgeç' }).click();
  await question.getByRole('button', { name: 'Evet' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('adı .docx yapılmış metin dosyası seçilir seçilmez hatalı', async ({ page }) => {
  const dialog = await openNewBookDialog(page);

  await dialog.getByLabel('Bildiri dosyaları').setInputFiles({
    name: '03_Metin.docx',
    mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    buffer: Buffer.from('Bu bir Word belgesi değil, düz metin.'),
  });

  const row = dialog.getByRole('list', { name: 'Seçilen dosyalar' }).getByRole('listitem');
  await expect(row.getByText('Hatalı')).toBeVisible();
  await expect(row.getByText('Geçerli bir Word (.docx) dosyası değil.')).toBeVisible();
});

test('sunucu hatası: kitap adındaki emoji modaldaki ad alanında gösterilir', async ({ page }) => {
  const dialog = await openNewBookDialog(page);
  await dialog.getByLabel('Kitap adı').fill('Kongre 2026 😀');
  await dialog.getByLabel('Bildiri dosyaları').setInputFiles(paperFiles);
  await dialog.getByRole('button', { name: 'Yükle ve devam et' }).click();

  await expect(dialog.getByLabel('Kitap adı')).toHaveAccessibleDescription(
    /Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın\./,
  );
  await expect(page).toHaveURL(/\/$/);
  await expect(dialog).toBeVisible();
});

test('sunucunun reddettiği dosyalardan biri kaldırılınca diğerleri reddedilmiş kalır', async ({ page }) => {
  const dialog = await openNewBookDialog(page);
  await dialog.getByLabel('Kitap adı').fill('Reddedilen Dosyalar Denemesi');
  const docxType = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
  // Seven sample papers and three ZIP packages that pass the browser's check but not the server's.
  await dialog
    .getByLabel('Bildiri dosyaları')
    .setInputFiles([
      ...paperFiles
        .slice(0, 7)
        .map((path) => ({ name: basename(path), mimeType: docxType, buffer: readFileSync(path) })),
      { name: '08_Tablo.docx', mimeType: docxType, buffer: excelWorkbook() },
      { name: '09_Sunum.docx', mimeType: docxType, buffer: powerPointPresentation() },
      { name: '10_Bos.docx', mimeType: docxType, buffer: emptyWordDocument() },
    ]);
  const submit = dialog.getByRole('button', { name: 'Yükle ve devam et' });
  await expect(submit).toBeEnabled();
  await submit.click();

  const rows = dialog.getByRole('list', { name: 'Seçilen dosyalar' }).getByRole('listitem');
  const row = (name: string) => rows.filter({ hasText: name });
  for (const name of ['08_Tablo.docx', '09_Sunum.docx', '10_Bos.docx']) {
    await expect(row(name).getByText('Reddedildi')).toBeVisible();
  }
  await expect(submit).toBeDisabled();

  await dialog.getByRole('button', { name: '09_Sunum.docx dosyasını kaldır' }).click();

  await expect(row('09_Sunum.docx')).toHaveCount(0);
  await expect(row('08_Tablo.docx').getByText('Reddedildi')).toBeVisible();
  await expect(row('08_Tablo.docx').getByText('Geçerli bir Word (.docx) belgesi değil.')).toBeVisible();
  await expect(row('10_Bos.docx').getByText('Reddedildi')).toBeVisible();
  await expect(rows.getByText('Reddedildi')).toHaveCount(2);
  await expect(submit).toBeDisabled();
});

test('üretim hatası: hata ekranı, tekrar dene ve sırayı düzenle', async ({ page, request }) => {
  const uid = await createThroughApi(request, 'Hata Ekranı Denemesi');
  await page.route(`**/api/books/${uid}`, async (route) => {
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

  await page.goto(`/kitaplar/${uid}`);

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
  const uid = await createThroughApi(request, 'Bekleme Ekranı Denemesi');
  await page.route(`**/api/books/${uid}`, async (route) => {
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

  await page.goto(`/kitaplar/${uid}`);
  await expect(page.getByRole('list', { name: 'Aşamalar' })).toBeVisible();
  await expectAccessible(page, 'Bekleme ekranı');
});

test('sil, Silinenler, geri al: kitap PDF’iyle Kitaplarım’a döner', async ({ page, request }, testInfo) => {
  const name = `Silinecek Kitap ${testInfo.project.name} ${run}`;
  const uid = await createThroughApi(request, name);
  await generateThroughApi(request, uid);

  await page.goto('/');
  const row = page.getByRole('listitem').filter({ has: page.getByRole('link', { name }) });
  await expect(row).toBeVisible();
  await expect(row.getByText('Hazır')).toBeVisible();
  await expectAccessible(page, 'Kitaplarım');

  await row.getByRole('button', { name: `${name} kitabını sil` }).click();
  const dialog = page.getByRole('dialog', { name: 'Kitabı sil' });
  await expect(dialog).toContainText(name);
  await expect(dialog).toContainText('Kitap Silinenler’e taşınacak. Oradan geri alabilirsiniz.');
  await dialog.getByRole('button', { name: 'Sil' }).click();
  await expect(page.getByRole('link', { name })).toHaveCount(0);

  await page
    .getByRole('navigation', { name: 'Ana menü' })
    .getByRole('link', { name: /Silinenler/ })
    .click();
  await expect(page.getByRole('heading', { level: 1, name: 'Silinenler' })).toBeVisible();
  const deletedRow = page
    .getByRole('list', { name: 'Silinen kitaplar' })
    .getByRole('listitem')
    .filter({ hasText: name });
  await expect(deletedRow).toBeVisible();
  // A deleted book cannot be opened: its name is not a link.
  await expect(page.getByRole('link', { name })).toHaveCount(0);
  await expectAccessible(page, 'Silinenler');

  await deletedRow.getByRole('button', { name: `${name} kitabını geri al` }).click();
  await expect(deletedRow).toHaveCount(0);

  await page
    .getByRole('navigation', { name: 'Ana menü' })
    .getByRole('link', { name: /Kitaplar/ })
    .click();
  await page.getByRole('link', { name }).click();
  await expect(page.locator('.react-pdf__Page canvas').first()).toBeVisible();
});

test('oluşmuş kitapta başlık düzenleme, onay ve yeniden oluşturma', async ({ page, request }, testInfo) => {
  const name = `Başlık Düzenleme ${testInfo.project.name} ${run}`;
  const newTitle = `SULAMADA SENSÖR VERİSİ ${testInfo.project.name.toLocaleUpperCase('tr-TR')}`;
  const uid = await createThroughApi(request, name);
  await generateThroughApi(request, uid);

  await page.goto('/');
  await page.getByRole('button', { name: `${name} kitabını düzenle` }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Kitabı düzenle' })).toBeVisible();
  await expect(
    page.getByText('Bu kitap oluşturuldu. Adı, sırayı veya bir başlığı değiştirirseniz'),
  ).toBeVisible();
  await expectAccessible(page, 'Düzenle sayfası');

  await page.getByRole('button', { name: '01_Akilli_Sulama.docx başlığını düzenle' }).click();
  const box = page.getByRole('textbox', { name: '01_Akilli_Sulama.docx başlığı' });
  await expect(box).toBeFocused();
  await box.fill(newTitle);
  await expectAccessible(page, 'Düzenle sayfası, başlık düzenlenirken');
  await box.press('Enter');

  const confirm = page.getByRole('dialog', { name: 'PDF silinsin mi?' });
  await expect(confirm).toBeVisible();
  await confirm.getByRole('button', { name: 'Devam et' }).click();
  await expect(page.getByText(newTitle)).toBeVisible();
  await expect(page.getByRole('link', { name: 'Kitabı görüntüle' })).toHaveCount(0);

  await page.getByRole('button', { name: 'Kitabı Oluştur' }).click();
  await page.waitForURL(new RegExp(`/kitaplar/${uid}$`));
  await expect(page.getByRole('button', { name: 'Temizlenen iletişim bilgileri' })).toBeVisible({
    timeout: 60_000,
  });

  // The viewer's table of contents and the PDF's own (page 2) print the new title, not the old one.
  await openFromContents(page, new RegExp(newTitle));
  await expect.poll(() => currentPage(page)).toBe(3);
  await page.goto(`/kitaplar/${uid}?sayfa=2`);
  const contents = page.locator('[data-page-slot="2"] .textLayer');
  await expect(contents).toContainText('SULAMADA SENSÖR');
  await expect(contents).not.toContainText('KENTSEL TARIMDA AKILLI');
});

test('görüntüleyici: varsayılan açılış, ortalanmış kapak, yakınlaştırma ve çift dokunma', async ({
  page,
  request,
}, testInfo) => {
  const uid = await createThroughApi(request, `Görüntüleyici Denemesi ${testInfo.project.name} ${run}`);
  await generateThroughApi(request, uid);
  const phone = isPhone(page);
  if (!phone) {
    await page.setViewportSize({ width: 1920, height: 1080 });
  }

  await page.goto(`/kitaplar/${uid}`);
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
    await page.goto(`/kitaplar/${uid}?sayfa=3`);
    const slot = page.locator('[data-page-slot="3"]');
    await expect(slot.locator('canvas')).toBeVisible();
    const fitted = (await slot.boundingBox())?.width ?? 0;
    const box = await slot.boundingBox();
    const x = (box?.x ?? 0) + (box?.width ?? 0) * 0.3;
    const y = (box?.y ?? 0) + Math.min(200, (box?.height ?? 0) / 2);
    await doubleTap(page, x, y);
    await expect.poll(async () => (await slot.boundingBox())?.width ?? 0).toBeGreaterThan(fitted * 1.8);
    expect(await horizontalOverflow()).toBeGreaterThan(0);
    expect(await currentPage(page)).toBe(3);

    await page.getByRole('button', { name: 'Sonraki sayfa' }).click();
    await expect.poll(() => currentPage(page)).toBe(4);
    const next = page.locator('[data-page-slot="4"] canvas');
    await expect(next).toBeVisible();

    // The zoomed page fills the screen; tapping twice on the visible part of it fits it to the width again.
    const canvasBox = await next.boundingBox();
    const areaBox = await area.boundingBox();
    const viewport = page.viewportSize();
    if (!canvasBox || !areaBox || !viewport) {
      throw new Error('Görüntüleyici alanı veya sayfa bulunamadı.');
    }
    const left = Math.max(canvasBox.x, areaBox.x, 0);
    const right = Math.min(canvasBox.x + canvasBox.width, areaBox.x + areaBox.width, viewport.width);
    const top = Math.max(canvasBox.y, areaBox.y, 0);
    const bottom = Math.min(canvasBox.y + canvasBox.height, areaBox.y + areaBox.height, viewport.height);
    await doubleTap(page, (left + right) / 2, (top + bottom) / 2);
    await expect.poll(horizontalOverflow).toBeLessThanOrEqual(0);
    await expect(page.getByRole('button', { name: 'Uzaklaştır' })).toBeDisabled();
  }
});

test('görüntüleyici: PDF yükleme hatasından sonra tekrar dene, pencere daralınca sayfa da daralır', async ({
  page,
  request,
}, testInfo) => {
  test.skip(isPhone(page), 'Pencere boyutu masaüstünde değiştirilir.');
  const uid = await createThroughApi(request, `Tekrar Dene Denemesi ${testInfo.project.name} ${run}`);
  await generateThroughApi(request, uid);

  // The first PDF request fails; the ones after it reach the server.
  let failed = false;
  await page.route(`**/api/books/${uid}/pdf`, async (route) => {
    if (failed) {
      await route.continue();
      return;
    }
    failed = true;
    await route.fulfill({ status: 500, body: '' });
  });

  await page.goto(`/kitaplar/${uid}`);
  const alert = page.getByRole('alert');
  await expect(alert).toContainText('PDF görüntülenemedi');
  await alert.getByRole('button', { name: 'Tekrar dene' }).click();

  const cover = page.locator('[data-page-slot="1"]');
  await expect(cover.locator('canvas')).toBeVisible();
  const width = async () => (await cover.boundingBox())?.width ?? 0;
  const before = await width();

  // Fit width: the page follows the area when the window gets narrower.
  await page.setViewportSize({ width: 1024, height: 800 });
  await expect.poll(width).toBeLessThan(before - 50);
});

test('yeni kitap modalı: içeriğe göre boyut, tek kaydırma, arka sayfa kaymaz', async ({ page }) => {
  const dialog = await openNewBookDialog(page);
  const box = () =>
    dialog.evaluate((element) => ({
      height: element.getBoundingClientRect().height,
      viewport: window.innerHeight,
      scrollHeight: element.scrollHeight,
      clientHeight: element.clientHeight,
      pageLocked: getComputedStyle(document.documentElement).overflow === 'hidden',
    }));

  if (!isPhone(page)) {
    // Short content: the modal ends well above the bottom of the screen.
    const empty = await box();
    expect(empty.height).toBeLessThan(empty.viewport - 64);
  }

  // A long list: only the body scrolls, never the <dialog> itself.
  await dialog.getByLabel('Bildiri dosyaları').setInputFiles(paperFiles);
  await expect(dialog.getByText("10 dosyadan 10'u seçildi")).toBeVisible();
  const full = await box();
  expect(full.scrollHeight).toBeLessThanOrEqual(full.clientHeight);
  expect(full.pageLocked).toBe(true);
  const body = dialog.locator('[data-dialog-body]');
  expect(await body.evaluate((element) => element.scrollHeight > element.clientHeight)).toBe(true);

  // "X" asks first once something was chosen; afterwards the page scrolls again.
  await dialog.getByRole('button', { name: 'Kapat' }).click();
  await page
    .getByRole('dialog', { name: 'Yeni kitap kapatılsın mı?' })
    .getByRole('button', { name: 'Evet' })
    .click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  expect(await page.evaluate(() => getComputedStyle(document.documentElement).overflow)).not.toBe('hidden');
});

test('düzen: dar ekranlarda yatay taşma yok ve dokunma hedefleri en az 44 px', async ({
  page,
  request,
}, testInfo) => {
  const suffix = `${testInfo.project.name} ${run}`;
  const longName = `Düzen Denemesi Uzun Bir Kitap Adı ile Satır Kırılımı Kontrolü ${suffix}`;
  const uid = await createThroughApi(request, longName);
  const finished = await createThroughApi(request, `Düzen Denemesi Hazır Kitap ${suffix}`);
  await generateThroughApi(request, finished);
  const busy = await createThroughApi(request, `Düzen Denemesi Hazırlanan Kitap ${suffix}`);
  const deletedName = `Düzen Denemesi Silinen Kitap ${suffix}`;
  const deleted = await createThroughApi(request, deletedName);
  expect((await request.delete(`/api/books/${deleted}`)).status()).toBe(204);

  // The generation screen: the book is shown as being generated.
  await page.route(`**/api/books/${busy}`, async (route) => {
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

  const screens: { path: string; heading: string; ready?: () => Promise<void> }[] = [
    {
      path: '/',
      heading: 'Kitaplarım',
      ready: () => expect(page.getByRole('link', { name: longName })).toBeVisible(),
    },
    { path: '#yeni', heading: 'Yeni kitap' },
    {
      path: '/silinenler',
      heading: 'Silinenler',
      ready: () => expect(page.getByText(deletedName)).toBeVisible(),
    },
    { path: `/kitaplar/${uid}`, heading: longName },
    {
      path: `/kitaplar/${busy}`,
      heading: `Düzen Denemesi Hazırlanan Kitap ${suffix}`,
      ready: () => expect(page.getByRole('list', { name: 'Aşamalar' })).toBeVisible(),
    },
    {
      path: `/kitaplar/${finished}`,
      heading: `Düzen Denemesi Hazır Kitap ${suffix}`,
      ready: () => expect(page.locator('.react-pdf__Page canvas').first()).toBeVisible(),
    },
    { path: `/kitaplar/${finished}/duzenle`, heading: 'Kitabı düzenle' },
    { path: '/olmayan-sayfa', heading: 'Sayfa bulunamadı' },
  ];

  // Phones at their narrowest and at a common width; the desktop project at its own width.
  const widths = isPhone(page) ? [320, 390] : [page.viewportSize()?.width ?? 1280];
  for (const width of widths) {
    await page.setViewportSize({ width, height: 844 });
    for (const screen of screens) {
      const where = `${String(width)} px ${screen.path}`;
      if (screen.path === '#yeni') {
        // The modal with ten files chosen.
        const dialog = await openNewBookDialog(page);
        await dialog.getByLabel('Bildiri dosyaları').setInputFiles(paperFiles);
        await expect(dialog.getByText("10 dosyadan 10'u seçildi")).toBeVisible();
      } else {
        await page.goto(screen.path);
        await expect(page.getByRole('heading', { level: 1, name: screen.heading })).toBeVisible();
      }

      await screen.ready?.();

      const layout = await page.evaluate(() => {
        const small = Array.from(
          document.querySelectorAll<HTMLElement>('button, a[href], input, [role="menuitem"]'),
        )
          .filter((element) => {
            const box = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            const hidden =
              box.width === 0 ||
              style.visibility === 'hidden' ||
              element.closest('.sr-only, [aria-hidden="true"], .annotationLayer, [hidden]');
            return !hidden && (box.height < 44 || box.width < 44);
          })
          .map(
            (element) =>
              `${element.tagName} "${(element.getAttribute('aria-label') ?? element.textContent).trim().slice(0, 30)}"`,
          );
        const main = document.querySelector('main')?.getBoundingClientRect().bottom ?? 0;
        return {
          innerWidth: window.innerWidth,
          scrollWidth: document.documentElement.scrollWidth,
          belowMain: document.documentElement.scrollHeight - (main + window.scrollY),
          dialogOpen: document.querySelector('dialog[open]') !== null,
          small,
        };
      });

      // A phone browser widens its layout viewport to fit content that is too wide (and shrinks the page), so
      // scrollWidth alone would still equal innerWidth: the viewport must keep the width the screen has.
      expect(layout.innerWidth, `${where}: görünüm alanı genişledi`).toBe(width);
      expect(layout.scrollWidth, `${where}: yatay taşma`).toBeLessThanOrEqual(layout.innerWidth);
      if (!layout.dialogOpen) {
        expect(layout.belowMain, `${where}: içeriğin altında boşluk`).toBeLessThanOrEqual(1);
      }
      expect(layout.small, `${where}: 44 px'ten küçük dokunma hedefleri`).toEqual([]);
    }
  }
});
