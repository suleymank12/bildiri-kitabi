import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { BookSummary } from '../../api/types';
import { bookSummary } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { server } from '../../test/server';
import { LibraryPage } from './LibraryPage';

function listOf(items: BookSummary[]) {
  return { items, page: 1, pageSize: 20, totalCount: items.length };
}

describe('LibraryPage', () => {
  it('shows the empty state with a link to a new book', async () => {
    server.use(http.get('/api/books', () => HttpResponse.json(listOf([]))));
    renderPage(<LibraryPage />, { path: '/kitaplar', route: '/kitaplar' });

    expect(await screen.findByRole('heading', { name: 'Henüz kitap oluşturmadınız.' })).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Yeni kitap' }).length).toBeGreaterThan(0);
  });

  it('lists books with status, counts and date', async () => {
    server.use(http.get('/api/books', () => HttpResponse.json(listOf([bookSummary()]))));
    renderPage(<LibraryPage />, { path: '/kitaplar', route: '/kitaplar' });

    const row = (await screen.findByRole('link', { name: 'Örnek Bilim Kongresi 2026' })).closest('li')!;
    expect(within(row).getByText('Hazır')).toBeInTheDocument();
    expect(within(row).getByText('29 Eylül 2026 10:00')).toBeInTheDocument();
  });

  it('deletes a book after confirmation', async () => {
    let items = [bookSummary()];
    let deleted: string | undefined;
    server.use(
      http.get('/api/books', () => HttpResponse.json(listOf(items))),
      http.delete('/api/books/:id', ({ params }) => {
        deleted = String(params.id);
        items = [];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderPage(<LibraryPage />, { path: '/kitaplar', route: '/kitaplar' });

    await user.click(await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 için işlemler' }));
    await user.click(screen.getByRole('menuitem', { name: 'Sil' }));

    const dialog = screen.getByRole('dialog', { name: 'Kitabı sil' });
    expect(dialog).toHaveTextContent('kalıcı olarak silinecek');
    expect(deleted).toBeUndefined();
    await user.click(within(dialog).getByRole('button', { name: 'Sil' }));

    await waitFor(() => {
      expect(deleted).toBe(bookSummary().id);
    });
    expect(await screen.findByRole('heading', { name: 'Henüz kitap oluşturmadınız.' })).toBeInTheDocument();
  });

  it('closes the confirmation without deleting on "Vazgeç"', async () => {
    let deleted = false;
    server.use(
      http.get('/api/books', () => HttpResponse.json(listOf([bookSummary()]))),
      http.delete('/api/books/:id', () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderPage(<LibraryPage />, { path: '/kitaplar', route: '/kitaplar' });

    await user.click(await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 için işlemler' }));
    await user.click(screen.getByRole('menuitem', { name: 'Sil' }));
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
    const { user } = renderPage(<LibraryPage />, { path: '/kitaplar', route: '/kitaplar' });

    await user.click(await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 için işlemler' }));
    const item = screen.getByRole('menuitem', { name: 'Sil' });

    expect(item).toHaveAttribute('aria-disabled', 'true');
    expect(item).toHaveAttribute('title', 'Kitap hazırlanırken silinemez.');
    expect(screen.getByText('Kitap hazırlanırken silinemez.')).toBeVisible();
    await user.click(item);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
