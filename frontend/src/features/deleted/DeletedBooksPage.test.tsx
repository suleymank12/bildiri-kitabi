import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { DeletedBookSummary } from '../../api/types';
import { bookSummary } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { server } from '../../test/server';
import { DeletedBooksPage, RESTORED_MESSAGE } from './DeletedBooksPage';

function deleted(overrides: Partial<DeletedBookSummary> = {}): DeletedBookSummary {
  return { ...bookSummary(), pdfUrl: null, deletedAt: '2026-09-30T08:15:00Z', ...overrides };
}

function listOf(items: DeletedBookSummary[]) {
  return { items, page: 1, pageSize: 20, totalCount: items.length };
}

describe('DeletedBooksPage', () => {
  it('says there is nothing when no book was deleted', async () => {
    server.use(http.get('/api/books/deleted', () => HttpResponse.json(listOf([]))));
    renderPage(<DeletedBooksPage />, { path: '/silinenler', route: '/silinenler' });

    expect(await screen.findByRole('heading', { name: 'Silinmiş kitap yok.' })).toBeInTheDocument();
  });

  it('lists deleted books with paper count, status and deletion time; names are not links', async () => {
    server.use(
      http.get('/api/books/deleted', () =>
        HttpResponse.json(
          listOf([
            deleted(),
            deleted({ uid: 'b2', name: 'Başarısız Kitap', status: 'Failed', paperCount: 10 }),
          ]),
        ),
      ),
    );
    renderPage(<DeletedBooksPage />, { path: '/silinenler', route: '/silinenler' });

    const list = await screen.findByRole('list', { name: 'Silinen kitaplar' });
    const [first, second] = within(list).getAllByRole('listitem');
    expect(first).toHaveTextContent('Örnek Bilim Kongresi 2026');
    expect(within(first!).getByText('Hazır')).toBeInTheDocument();
    expect(within(first!).getByText('30 Eylül 2026 11:15')).toBeInTheDocument();
    expect(within(first!).getByText('10')).toBeInTheDocument();
    expect(within(second!).getByText('Hata')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Örnek Bilim Kongresi 2026' })).not.toBeInTheDocument();
  });

  it('restores a book: the row leaves the list and the result is read out', async () => {
    let items = [deleted(), deleted({ uid: 'b2', name: 'Kalan Kitap' })];
    let restored: string | undefined;
    server.use(
      http.get('/api/books/deleted', () => HttpResponse.json(listOf(items))),
      http.post('/api/books/:uid/restore', ({ params }) => {
        restored = String(params.uid);
        items = items.filter((book) => book.uid !== params.uid);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user, queryClient } = renderPage(<DeletedBooksPage />, {
      path: '/silinenler',
      route: '/silinenler',
    });
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries');

    await user.click(
      await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını geri al' }),
    );

    await waitFor(() => {
      expect(screen.queryByText('Örnek Bilim Kongresi 2026')).not.toBeInTheDocument();
    });
    expect(restored).toBe(bookSummary().uid);
    expect(screen.getByText('Kalan Kitap')).toBeInTheDocument();
    expect(await screen.findByText(RESTORED_MESSAGE)).toBeInTheDocument();
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['books', 'list'] });
  });

  it('explains a failed restore', async () => {
    server.use(
      http.get('/api/books/deleted', () => HttpResponse.json(listOf([deleted()]))),
      http.post('/api/books/:uid/restore', () =>
        HttpResponse.json(
          {
            type: null,
            title: 't',
            status: 404,
            detail: 'd',
            code: 'DELETED_BOOK_NOT_FOUND',
            traceId: 't',
            errors: [],
          },
          { status: 404 },
        ),
      ),
    );
    const { user } = renderPage(<DeletedBooksPage />, { path: '/silinenler', route: '/silinenler' });

    await user.click(
      await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını geri al' }),
    );

    expect(
      await screen.findByText('Kitap silinenler arasında bulunamadı; zaten geri alınmış olabilir.'),
    ).toBeInTheDocument();
  });
});
