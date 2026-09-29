import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { DeletedBookSummary } from '../../api/types';
import { bookSummary, manyBooks, pageOf } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { server } from '../../test/server';
import { DeletedBooksPage, RESTORED_MESSAGE } from './DeletedBooksPage';

function deleted(overrides: Partial<DeletedBookSummary> = {}): DeletedBookSummary {
  return { ...bookSummary(), pdfUrl: null, deletedAt: '2026-09-30T08:15:00Z', ...overrides };
}

function manyDeleted(count: number): DeletedBookSummary[] {
  return manyBooks(count).map((book) => deleted(book));
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

  describe('a page past the end', () => {
    /** The deleted books on the server, served page by page; the requested page numbers are recorded. */
    function serve(initial: DeletedBookSummary[]) {
      const state = { items: initial, requested: [] as number[] };
      server.use(
        http.get('/api/books/deleted', ({ request }) => {
          const body = pageOf(state.items, request.url);
          state.requested.push(body.page);
          return HttpResponse.json(body);
        }),
        http.post('/api/books/:uid/restore', ({ params }) => {
          state.items = state.items.filter((book) => book.uid !== params.uid);
          return new HttpResponse(null, { status: 204 });
        }),
      );
      return state;
    }

    it('goes back to page 1 when the only book of page 2 is restored (21 → 20)', async () => {
      const state = serve(manyDeleted(21));
      const { user } = renderPage(<DeletedBooksPage />, {
        path: '/silinenler',
        route: '/silinenler?sayfa=2',
      });

      await user.click(await screen.findByRole('button', { name: 'Kitap 21 kitabını geri al' }));

      expect(await screen.findByText('Kitap 1')).toBeInTheDocument();
      expect(
        within(screen.getByRole('list', { name: 'Silinen kitaplar' })).getAllByRole('listitem'),
      ).toHaveLength(20);
      expect(screen.queryByRole('navigation', { name: 'Sayfalar' })).not.toBeInTheDocument();
      expect(state.requested.at(-1)).toBe(1);
    });

    it('opens the last page that exists for a page number typed into the address', async () => {
      const state = serve(manyDeleted(45));
      renderPage(<DeletedBooksPage />, { path: '/silinenler', route: '/silinenler?sayfa=99' });

      expect(await screen.findByText('Kitap 41')).toBeInTheDocument();
      expect(
        within(screen.getByRole('list', { name: 'Silinen kitaplar' })).getAllByRole('listitem'),
      ).toHaveLength(5);
      expect(screen.getByRole('navigation', { name: 'Sayfalar' })).toHaveTextContent('Sayfa 3 / 3');
      expect(state.requested).toEqual([99, 3]);
    });

    it('shows the empty state when the last deleted book is restored', async () => {
      const state = serve(manyDeleted(21));
      const { user } = renderPage(<DeletedBooksPage />, {
        path: '/silinenler',
        route: '/silinenler?sayfa=2',
      });
      const restore = await screen.findByRole('button', { name: 'Kitap 21 kitabını geri al' });

      // The other twenty were restored in another tab in the meantime.
      state.items = state.items.slice(20);
      await user.click(restore);

      expect(await screen.findByRole('heading', { name: 'Silinmiş kitap yok.' })).toBeInTheDocument();
      expect(state.requested.at(-1)).toBe(1);
    });

    it('keeps the loading view while the page it moves to loads and does not act on the stale page', async () => {
      let release: () => void = () => undefined;
      const held = new Promise<void>((resolve) => {
        release = resolve;
      });
      const books = manyDeleted(21);
      const requested: number[] = [];
      server.use(
        http.get('/api/books/deleted', async ({ request }) => {
          const body = pageOf(books, request.url);
          requested.push(body.page);
          if (body.page === 2) {
            await held;
          }
          return HttpResponse.json(body);
        }),
      );
      renderPage(<DeletedBooksPage />, { path: '/silinenler', route: '/silinenler?sayfa=99' });

      await waitFor(() => {
        expect(requested).toEqual([99, 2]);
      });
      expect(screen.getByText('Silinen kitaplar yükleniyor')).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Silinmiş kitap yok.' })).not.toBeInTheDocument();
      expect(screen.queryByRole('list', { name: 'Silinen kitaplar' })).not.toBeInTheDocument();
      expect(screen.queryByRole('navigation', { name: 'Sayfalar' })).not.toBeInTheDocument();

      release();
      expect(await screen.findByText('Kitap 21')).toBeInTheDocument();
      expect(screen.getByRole('navigation', { name: 'Sayfalar' })).toHaveTextContent('Sayfa 2 / 2');
      expect(requested).toEqual([99, 2]);
    });
  });
});
