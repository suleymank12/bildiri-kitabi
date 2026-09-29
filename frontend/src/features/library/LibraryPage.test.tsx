import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { BookSummary } from '../../api/types';
import { LIBRARY_TABLE_COLUMNS, NUMERIC_COLUMN } from '../../lib/tableColumns';
import { setViewportWidth } from '../../test/media';
import { bookSummary, manyBooks, pageOf } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { server } from '../../test/server';
import { LibraryPage } from './LibraryPage';

function listOf(items: BookSummary[]) {
  return { items, page: 1, pageSize: 20, totalCount: items.length };
}

describe('LibraryPage', () => {
  it('shows the empty state pointing to the one "Yeni kitap" button next to the heading', async () => {
    server.use(http.get('/api/books', () => HttpResponse.json(listOf([]))));
    renderPage(<LibraryPage />, { path: '/', route: '/' });

    const heading = await screen.findByRole('heading', { name: 'Henüz kitap oluşturmadınız.' });
    expect(screen.getAllByRole('button', { name: 'Yeni kitap' })).toHaveLength(1);
    const text = heading.nextElementSibling!;
    expect(text).toHaveTextContent(
      'Sağ üstteki Yeni kitap düğmesiyle 10 bildiri dosyasını yükleyerek ilk kitabınızı oluşturun.',
    );
    // Bold text, not a second link or button.
    expect(within(text as HTMLElement).getByText('Yeni kitap').tagName).toBe('STRONG');
    expect(within(text as HTMLElement).queryByRole('link')).not.toBeInTheDocument();
  });

  it('says "Yukarıdaki" on phones, where the button sits below the heading', async () => {
    setViewportWidth(390);
    server.use(http.get('/api/books', () => HttpResponse.json(listOf([]))));
    renderPage(<LibraryPage />, { path: '/', route: '/' });

    const heading = await screen.findByRole('heading', { name: 'Henüz kitap oluşturmadınız.' });
    expect(heading.nextElementSibling).toHaveTextContent(
      /^Yukarıdaki Yeni kitap düğmesiyle 10 bildiri dosyasını/,
    );
    expect(screen.getAllByRole('button', { name: 'Yeni kitap' })).toHaveLength(1);
  });

  it('lists books with status, counts and date', async () => {
    server.use(http.get('/api/books', () => HttpResponse.json(listOf([bookSummary()]))));
    renderPage(<LibraryPage />, { path: '/', route: '/' });

    const row = (await screen.findByRole('link', { name: 'Örnek Bilim Kongresi 2026' })).closest('li')!;
    expect(within(row).getByText('Hazır')).toBeInTheDocument();
    expect(within(row).getByText('29 Eylül 2026 10:00')).toBeInTheDocument();
  });

  it('lays out the heading and the rows with the same columns and the same alignment per column', async () => {
    server.use(http.get('/api/books', () => HttpResponse.json(listOf([bookSummary()]))));
    const { container } = renderPage(<LibraryPage />, { path: '/', route: '/' });

    const row = (await screen.findByRole('link', { name: 'Örnek Bilim Kongresi 2026' })).closest('li')!;
    const heading = container.querySelector('section > [aria-hidden="true"]')!;
    expect(heading.className).toContain(LIBRARY_TABLE_COLUMNS);
    expect(row.className).toContain(LIBRARY_TABLE_COLUMNS);

    const alignment = (element: Element) =>
      [...element.classList].filter((name) => /^md:text-(left|center|right)$/.test(name));
    // Status, paper count, page count and date sit in a "display: contents" group on wide screens.
    const headingCells = [...heading.children].slice(1, 5);
    const rowCells = [...row.children[2]!.children];
    expect(headingCells.map((cell) => cell.textContent)).toEqual([
      'Durum',
      'Bildiri',
      'Sayfa',
      'Oluşturulma',
    ]);
    expect(rowCells).toHaveLength(headingCells.length);
    headingCells.forEach((cell, index) => {
      expect(alignment(rowCells[index]!)).toEqual(alignment(cell));
    });
    for (const index of [1, 2]) {
      expect(headingCells[index]).toHaveClass(NUMERIC_COLUMN);
      expect(rowCells[index]).toHaveClass(NUMERIC_COLUMN, 'numeric');
    }
  });

  it('deletes a book after confirmation', async () => {
    let items = [bookSummary()];
    let deleted: string | undefined;
    server.use(
      http.get('/api/books', () => HttpResponse.json(listOf(items))),
      http.delete('/api/books/:uid', ({ params }) => {
        deleted = String(params.uid);
        items = [];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderPage(<LibraryPage />, { path: '/', route: '/' });

    await user.click(await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını sil' }));

    const dialog = screen.getByRole('dialog', { name: 'Kitabı sil' });
    expect(dialog).toHaveTextContent('Kitap Silinenler’e taşınacak. Oradan geri alabilirsiniz.');
    expect(deleted).toBeUndefined();
    await user.click(within(dialog).getByRole('button', { name: 'Sil' }));

    await waitFor(() => {
      expect(deleted).toBe(bookSummary().uid);
    });
    expect(await screen.findByText('“Örnek Bilim Kongresi 2026” Silinenler’e taşındı.')).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: 'Henüz kitap oluşturmadınız.' })).toBeInTheDocument();
  });

  it('closes the confirmation without deleting on "Vazgeç"', async () => {
    let deleted = false;
    server.use(
      http.get('/api/books', () => HttpResponse.json(listOf([bookSummary()]))),
      http.delete('/api/books/:uid', () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderPage(<LibraryPage />, { path: '/', route: '/' });

    await user.click(await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını sil' }));
    await user.click(screen.getByRole('button', { name: 'Vazgeç' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(deleted).toBe(false);
  });

  it('does not allow deleting a book that is being prepared and says why', async () => {
    server.use(
      http.get('/api/books', () =>
        HttpResponse.json(
          listOf([
            bookSummary({
              status: 'Processing',
              stage: 'Rendering',
              progressPercent: 45,
              pageCount: null,
              pdfUrl: null,
            }),
          ]),
        ),
      ),
    );
    const { user } = renderPage(<LibraryPage />, { path: '/', route: '/' });

    const button = await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını sil' });
    const edit = screen.getByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını düzenle' });

    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).toHaveAccessibleDescription('Kitap oluşturulurken silinemez.');
    await user.click(button);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    expect(edit).toHaveAttribute('aria-disabled', 'true');
    await user.hover(edit);
    expect(screen.getByRole('tooltip')).toHaveTextContent('Kitap oluşturulurken düzenlenemez.');
    await user.click(edit);
    expect(screen.queryByText('Başka sayfa')).not.toBeInTheDocument();
  });

  it('puts "Düzenle" right before "Sil" and opens the edit page', async () => {
    server.use(http.get('/api/books', () => HttpResponse.json(listOf([bookSummary()]))));
    const { user } = renderPage(<LibraryPage />, { path: '/', route: '/' });

    const edit = await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını düzenle' });
    const remove = screen.getByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını sil' });
    expect(edit.compareDocumentPosition(remove) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    await user.click(edit);
    expect(await screen.findByText('Başka sayfa')).toBeInTheDocument();
  });

  describe('a page past the end', () => {
    /** The books on the server, served page by page; the requested page numbers are recorded. */
    function serve(initial: BookSummary[]) {
      const state = { items: initial, requested: [] as number[] };
      server.use(
        http.get('/api/books', ({ request }) => {
          const body = pageOf(state.items, request.url);
          state.requested.push(body.page);
          return HttpResponse.json(body);
        }),
        http.delete('/api/books/:uid', ({ params }) => {
          state.items = state.items.filter((book) => book.uid !== params.uid);
          return new HttpResponse(null, { status: 204 });
        }),
      );
      return state;
    }

    it('goes back to page 1 when the only book of page 2 is deleted (21 → 20)', async () => {
      const state = serve(manyBooks(21));
      const { user } = renderPage(<LibraryPage />, { path: '/', route: '/?sayfa=2' });

      await user.click(await screen.findByRole('button', { name: 'Kitap 21 kitabını sil' }));
      await user.click(
        within(screen.getByRole('dialog', { name: 'Kitabı sil' })).getByRole('button', { name: 'Sil' }),
      );

      expect(await screen.findByRole('link', { name: 'Kitap 1' })).toBeInTheDocument();
      expect(within(screen.getByRole('list', { name: 'Kitaplar' })).getAllByRole('listitem')).toHaveLength(
        20,
      );
      // Everything fits on one page again: no page navigation.
      expect(screen.queryByRole('navigation', { name: 'Sayfalar' })).not.toBeInTheDocument();
      expect(state.requested.at(-1)).toBe(1);
    });

    it('opens the last page that exists for a page number typed into the address', async () => {
      const state = serve(manyBooks(45));
      renderPage(<LibraryPage />, { path: '/', route: '/?sayfa=99' });

      expect(await screen.findByRole('link', { name: 'Kitap 41' })).toBeInTheDocument();
      expect(within(screen.getByRole('list', { name: 'Kitaplar' })).getAllByRole('listitem')).toHaveLength(5);
      expect(screen.getByRole('navigation', { name: 'Sayfalar' })).toHaveTextContent('Sayfa 3 / 3');
      expect(state.requested).toEqual([99, 3]);
    });

    it('shows the empty state when the last book is deleted', async () => {
      const state = serve(manyBooks(21));
      const { user } = renderPage(<LibraryPage />, { path: '/', route: '/?sayfa=2' });
      await user.click(await screen.findByRole('button', { name: 'Kitap 21 kitabını sil' }));

      // The other twenty were deleted in another tab in the meantime.
      state.items = state.items.slice(20);
      await user.click(
        within(screen.getByRole('dialog', { name: 'Kitabı sil' })).getByRole('button', { name: 'Sil' }),
      );

      expect(await screen.findByRole('heading', { name: 'Henüz kitap oluşturmadınız.' })).toBeInTheDocument();
      expect(state.requested.at(-1)).toBe(1);
    });

    it('keeps the loading view while the page it moves to loads and does not act on the stale page', async () => {
      let release: () => void = () => undefined;
      const held = new Promise<void>((resolve) => {
        release = resolve;
      });
      const books = manyBooks(21);
      const requested: number[] = [];
      server.use(
        http.get('/api/books', async ({ request }) => {
          const body = pageOf(books, request.url);
          requested.push(body.page);
          if (body.page === 2) {
            await held;
          }
          return HttpResponse.json(body);
        }),
      );
      renderPage(<LibraryPage />, { path: '/', route: '/?sayfa=99' });

      // Page 2 is on its way; meanwhile the empty page 99 stands in for it (placeholder data).
      await waitFor(() => {
        expect(requested).toEqual([99, 2]);
      });
      expect(screen.getByText('Kitaplar yükleniyor')).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Henüz kitap oluşturmadınız.' })).not.toBeInTheDocument();
      expect(screen.queryByRole('list', { name: 'Kitaplar' })).not.toBeInTheDocument();
      expect(screen.queryByRole('navigation', { name: 'Sayfalar' })).not.toBeInTheDocument();

      release();
      expect(await screen.findByRole('link', { name: 'Kitap 21' })).toBeInTheDocument();
      expect(screen.getByRole('navigation', { name: 'Sayfalar' })).toHaveTextContent('Sayfa 2 / 2');
      expect(requested).toEqual([99, 2]);
    });
  });
});
