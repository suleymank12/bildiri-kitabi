import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { BookSummary } from '../../api/types';
import { LIBRARY_TABLE_COLUMNS, NUMERIC_COLUMN } from '../../lib/tableColumns';
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

  it('lays out the heading and the rows with the same columns and the same alignment per column', async () => {
    server.use(http.get('/api/books', () => HttpResponse.json(listOf([bookSummary()]))));
    const { container } = renderPage(<LibraryPage />, { path: '/kitaplar', route: '/kitaplar' });

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
      http.delete('/api/books/:id', ({ params }) => {
        deleted = String(params.id);
        items = [];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderPage(<LibraryPage />, { path: '/kitaplar', route: '/kitaplar' });

    await user.click(await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını sil' }));

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
    const { user } = renderPage(<LibraryPage />, { path: '/kitaplar', route: '/kitaplar' });

    const button = await screen.findByRole('button', { name: 'Örnek Bilim Kongresi 2026 kitabını sil' });

    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).toHaveAttribute('title', 'Kitap hazırlanırken silinemez.');
    expect(button).toHaveAccessibleDescription('Kitap hazırlanırken silinemez.');
    await user.click(button);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
