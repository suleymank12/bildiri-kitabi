import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { BookDetail } from '../../api/types';
import { bookDetail, paper } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { server } from '../../test/server';
import { BookPage } from './BookPage';

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
  it('shows the stage and progress while processing', async () => {
    renderBook({ ...book, status: 'Processing', stage: 'Rendering', progressPercent: 45 });

    expect(await screen.findByText('Sayfalar dizgiye giriyor')).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '45');
  });

  it('offers the PDF when the book is ready', async () => {
    renderBook({
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

    expect(await screen.findByRole('heading', { name: 'Kitap hazır' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /PDF’i aç/ })).toHaveAttribute(
      'href',
      `/api/books/${book.id}/pdf`,
    );
    expect(screen.getByRole('link', { name: /İndir/ })).toHaveAttribute(
      'href',
      `/api/books/${book.id}/pdf?download=true`,
    );
    expect(screen.getByText(/telefon numarası kitaba aktarılmadı/)).toHaveTextContent(
      '3 e-posta adresi ve 3 telefon numarası kitaba aktarılmadı.',
    );
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
  });
});
