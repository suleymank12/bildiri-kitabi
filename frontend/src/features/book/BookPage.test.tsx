import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { BookDetail } from '../../api/types';
import { bookDetail, paper } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { server } from '../../test/server';
import { BookPage } from './BookPage';

// The viewer has its own tests (viewer/PdfViewer.test.tsx); here it only has to appear for a finished book.
vi.mock('../viewer/PdfViewer', () => ({
  PdfViewer: ({ book }: { book: BookDetail }) => (
    <section aria-label="PDF görüntüleyici">{book.pdfUrl}</section>
  ),
}));

const book = bookDetail();
const [a, b, c] = book.papers as [
  BookDetail['papers'][number],
  BookDetail['papers'][number],
  BookDetail['papers'][number],
];

function renderBook(detail: BookDetail = book) {
  server.use(http.get('/api/books/:id', () => HttpResponse.json(detail)));
  return renderPage(<BookPage />, { path: '/kitaplar/:id', route: `/kitaplar/${detail.id}` });
}

const fileOrder = () =>
  within(screen.getByRole('list', { name: 'Bildiri sırası' }))
    .getAllByRole('listitem')
    .map((row) => within(row).getByText(/\.docx$/).textContent);

describe('BookPage — order step', () => {
  it('shows file names, detected titles and where each title came from', async () => {
    renderBook({ ...book, papers: [a, b, paper(3, { titleSource: 'FileName', title: '03 Bildiri' })] });

    expect(
      await screen.findByRole('heading', { name: 'Örnek Bilim Kongresi 2026', level: 1 }),
    ).toBeInTheDocument();
    expect(fileOrder()).toEqual(['01_Bildiri.docx', '02_Bildiri.docx', '03_Bildiri.docx']);
    expect(screen.getByText('BİLDİRİ 01 BAŞLIĞI')).toBeInTheDocument();
    expect(screen.getAllByText('Başlık stilinden')).toHaveLength(2);
    expect(screen.getByText('Dosya adından')).toBeInTheDocument();
    expect(screen.getByRole('listitem', { current: 'step' })).toHaveTextContent('Sıra ve kontrol');
  });

  it('"Aşağı" moves the paper and sends the whole new order', async () => {
    let body: unknown;
    server.use(
      http.put('/api/books/:id/paper-order', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({
          ...book,
          papers: [
            { ...b, order: 1 },
            { ...a, order: 2 },
            { ...c, order: 3 },
          ],
        });
      }),
    );
    const { user } = renderBook();

    await user.click(await screen.findByRole('button', { name: '01_Bildiri.docx dosyasını aşağı taşı' }));

    expect(fileOrder()).toEqual(['02_Bildiri.docx', '01_Bildiri.docx', '03_Bildiri.docx']);
    await waitFor(() => {
      expect(body).toEqual({ paperIds: [b.id, a.id, c.id] });
    });
    expect(await screen.findByText('Sıra, yükleme sırasından farklı.')).toBeInTheDocument();
    // The focus stays on the button that was pressed and the new position is read out.
    expect(screen.getByRole('button', { name: '01_Bildiri.docx dosyasını aşağı taşı' })).toHaveFocus();
    expect(await screen.findByText('01_Bildiri.docx, 2. sıraya taşındı.')).toBeInTheDocument();
  });

  it('has no drag handle; the first row cannot move up and the last cannot move down', async () => {
    renderBook();

    await screen.findByRole('list', { name: 'Bildiri sırası' });
    expect(screen.queryByRole('button', { name: /sürükle/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '01_Bildiri.docx dosyasını yukarı taşı' })).toBeDisabled();
    expect(screen.getByRole('button', { name: '03_Bildiri.docx dosyasını aşağı taşı' })).toBeDisabled();
  });

  it('moves the focus to the other button when a paper reaches the end of the list', async () => {
    server.use(
      http.put('/api/books/:id/paper-order', () =>
        HttpResponse.json({
          ...book,
          papers: [
            { ...a, order: 1 },
            { ...c, order: 2 },
            { ...b, order: 3 },
          ],
        }),
      ),
    );
    const { user } = renderBook();

    await user.click(await screen.findByRole('button', { name: '02_Bildiri.docx dosyasını aşağı taşı' }));

    expect(screen.getByRole('button', { name: '02_Bildiri.docx dosyasını yukarı taşı' })).toHaveFocus();
  });

  it('rolls the order back and explains when saving fails', async () => {
    server.use(
      http.put('/api/books/:id/paper-order', () =>
        HttpResponse.json(
          {
            type: null,
            title: 'x',
            status: 500,
            detail: 'Beklenmeyen',
            code: 'INTERNAL_ERROR',
            traceId: 't',
          },
          { status: 500 },
        ),
      ),
    );
    const { user } = renderBook();

    await user.click(await screen.findByRole('button', { name: '02_Bildiri.docx dosyasını yukarı taşı' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Sıra kaydedilemedi');
    expect(fileOrder()).toEqual(['01_Bildiri.docx', '02_Bildiri.docx', '03_Bildiri.docx']);
  });

  it('shows the error of a failed book with a retry button', async () => {
    let started = false;
    server.use(
      http.post('/api/books/:id/generate', () => {
        started = true;
        return new HttpResponse(null, { status: 202 });
      }),
    );
    const { user } = renderBook({
      ...book,
      status: 'Failed',
      error: {
        code: 'UNSUPPORTED_CHARACTER',
        message: "3. sıradaki bildiride PDF yazı tipinde bulunmayan '𝒜' karakteri var.",
      },
    });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent("3. sıradaki bildiride PDF yazı tipinde bulunmayan '𝒜' karakteri var.");
    await user.click(within(alert).getByRole('button', { name: 'Tekrar dene' }));

    await waitFor(() => {
      expect(started).toBe(true);
    });
  });
});

describe('BookPage — generation', () => {
  it('lists the stages with their state, the progress and the elapsed time while processing', async () => {
    renderBook({
      ...book,
      status: 'Processing',
      stage: 'Rendering',
      progressPercent: 45,
      processingStartedAt: new Date(Date.now() - 8_000).toISOString(),
    });

    const stages = within(await screen.findByRole('list', { name: 'Aşamalar' })).getAllByRole('listitem');
    expect(stages.map((item) => item.getAttribute('data-state'))).toEqual([
      'done',
      'done',
      'done',
      'done',
      'current',
      'pending',
      'pending',
    ]);
    expect(stages[4]).toHaveAttribute('aria-current', 'step');
    expect(stages[4]).toHaveTextContent('PDF oluşturuluyor (sürüyor)');
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '45');
    expect(screen.getByText('%45')).toBeInTheDocument();
    expect(screen.getByText(/^\d+ sn$/)).toBeInTheDocument();
  });

  it('shows the waiting step for a queued book', async () => {
    renderBook({ ...book, status: 'Queued' });

    const stages = within(await screen.findByRole('list', { name: 'Aşamalar' })).getAllByRole('listitem');
    expect(stages[0]).toHaveTextContent('Sırada bekliyor (sürüyor)');
  });

  it('shows pages and status in one line, with the removed contact details behind an info button', async () => {
    const { user } = renderBook({
      ...book,
      status: 'Completed',
      progressPercent: 100,
      pageCount: 22,
      pdfSizeBytes: 131_700,
      pdfUrl: `/api/books/${book.id}/pdf`,
      papers: book.papers.map((p, i) => ({
        ...p,
        startPage: 3 + 2 * i,
        endPage: 4 + 2 * i,
        removedEmailCount: 1,
        removedPhoneCount: 1,
      })),
    });

    const info = await screen.findByRole('button', { name: 'Temizlenen iletişim bilgileri' });
    expect(info.parentElement).toHaveTextContent(/^3 bildiri · 22 sayfa · .+ · Hazır/);
    expect(screen.queryByText('Kitap hazır')).not.toBeInTheDocument();

    // The removed contact details are one click away, not on the page.
    expect(info).toHaveAttribute('aria-expanded', 'false');
    await user.click(info);
    expect(info).toHaveAttribute('aria-expanded', 'true');
    const breakdown = screen.getByRole('table', { name: 'Bildiri bazında temizlenen iletişim bilgileri' });
    expect(within(breakdown).getAllByRole('row')).toHaveLength(4);
    expect(breakdown).toBeVisible();
    expect(breakdown.parentElement).toHaveTextContent('3 e-posta adresi ve 3 telefon numarası temizlendi');
    await user.keyboard('{Escape}');
    expect(info).toHaveAttribute('aria-expanded', 'false');
    expect(info).toHaveFocus();
    expect(screen.queryByRole('list', { name: 'Aşamalar' })).not.toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'PDF görüntüleyici' })).toHaveTextContent(
      `/api/books/${book.id}/pdf`,
    );
  });
});

describe('BookPage — failure', () => {
  const failed: BookDetail = {
    ...book,
    status: 'Failed',
    stage: 'Rendering',
    error: { code: 'RENDER_FAILED', message: 'PDF dizgisi oluşturulamadı.' },
  };

  it('shows the message, the code, the stage where it stopped and retries on "Tekrar dene"', async () => {
    let started = 0;
    server.use(
      http.post('/api/books/:id/generate', () => {
        started++;
        return new HttpResponse(null, { status: 202 });
      }),
    );
    const { user } = renderBook(failed);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Kitap oluşturulamadı');
    expect(alert).toHaveTextContent('PDF dizgisi oluşturulamadı.');
    expect(alert).toHaveTextContent('Hata kodu: RENDER_FAILED');
    const stages = within(screen.getByRole('list', { name: 'Aşamalar' })).getAllByRole('listitem');
    expect(stages[4]).toHaveAttribute('data-state', 'failed');

    await user.click(within(alert).getByRole('button', { name: 'Tekrar dene' }));
    await waitFor(() => {
      expect(started).toBe(1);
    });
  });

  it('goes back to the order step with "Sırayı düzenle"', async () => {
    const { user } = renderBook(failed);

    await user.click(await screen.findByRole('button', { name: 'Sırayı düzenle' }));

    expect(screen.getByRole('heading', { name: 'Sıra ve kontrol' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '01_Bildiri.docx dosyasını aşağı taşı' })).toBeEnabled();
    expect(screen.getByText('Önceki deneme başarısız oldu')).toBeInTheDocument();
  });

  it('says so when the book does not exist', async () => {
    server.use(
      http.get('/api/books/:id', () =>
        HttpResponse.json(
          { title: 'x', status: 404, detail: 'yok', code: 'BOOK_NOT_FOUND' },
          { status: 404 },
        ),
      ),
    );
    renderPage(<BookPage />, { path: '/kitaplar/:id', route: '/kitaplar/olmayan' });

    expect(await screen.findByRole('heading', { name: 'Kitap bulunamadı' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Yeni kitap' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Kitaplarım' })).toBeInTheDocument();
  });
});
