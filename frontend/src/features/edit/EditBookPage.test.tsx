import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import type { BookDetail } from '../../api/types';
import { bookDetail } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { server } from '../../test/server';
import { COMPLETED_NOTICE, CONFLICT_MESSAGE, EditBookPage } from './EditBookPage';

const problem = (status: number, code: string, detail: string, errors: unknown[] = []) =>
  HttpResponse.json({ type: null, title: 't', status, detail, code, traceId: 't', errors }, { status });

function renderEdit(detail: BookDetail) {
  let current = detail;
  let gets = 0;
  server.use(
    http.get('/api/books/:uid', () => {
      gets++;
      return HttpResponse.json(current);
    }),
  );
  const view = renderPage(<EditBookPage />, {
    path: '/kitaplar/:uid/duzenle',
    route: `/kitaplar/${detail.uid}/duzenle`,
  });
  return {
    ...view,
    gets: () => gets,
    setCurrent: (next: BookDetail) => {
      current = next;
    },
  };
}

const completed = () =>
  bookDetail({
    status: 'Completed',
    progressPercent: 100,
    pageCount: 22,
    pdfSizeBytes: 1000,
    pdfUrl: '/api/books/b0000000-0000-0000-0000-000000000001/pdf',
  });

describe('EditBookPage', () => {
  it('renames the book and shows validation errors under the field', async () => {
    let body: unknown;
    server.use(
      http.put('/api/books/:uid', async ({ request }) => {
        body = await request.json();
        const name = (body as { name: string }).name;
        return name.includes('😀')
          ? problem(400, 'BOOK_NAME_UNSUPPORTED_CHARACTER', "Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.", [
              { code: 'BOOK_NAME_UNSUPPORTED_CHARACTER', message: "Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.", field: 'name', fileName: null },
            ])
          : HttpResponse.json(bookDetail({ name }));
      }),
    );
    const { user } = renderEdit(bookDetail());
    const field = await screen.findByLabelText('Kitap adı');
    expect(field).toHaveValue('Örnek Bilim Kongresi 2026');

    await user.clear(field);
    await user.type(field, 'ab');
    await user.click(screen.getByRole('button', { name: 'Adı kaydet' }));
    expect(field).toHaveAccessibleDescription(expect.stringContaining('Kitap adı 3–150 karakter olmalıdır.'));
    expect(body).toBeUndefined();

    await user.clear(field);
    await user.type(field, 'Kongre 😀');
    await user.click(screen.getByRole('button', { name: 'Adı kaydet' }));
    expect(await screen.findByText("Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.")).toBeInTheDocument();

    await user.clear(field);
    await user.type(field, '  Yeni Kongre Adı ');
    await user.click(screen.getByRole('button', { name: 'Adı kaydet' }));
    await waitFor(() => {
      expect(body).toEqual({ name: 'Yeni Kongre Adı' });
    });
    expect(await screen.findByText('Kitap adı kaydedildi.')).toBeInTheDocument();
  });

  it('edits a paper title in place: Esc backs out, Enter saves, errors stay under the box', async () => {
    const bodies: unknown[] = [];
    server.use(
      http.put('/api/books/:uid/papers/:paperUid/title', async ({ request, params }) => {
        const body = (await request.json()) as { title: string };
        bodies.push({ paperUid: params.paperUid, ...body });
        if (body.title.includes('@')) {
          return problem(400, 'PAPER_TITLE_CONTACT_INFO', 'Başlıkta e-posta adresi veya telefon numarası bulunamaz.');
        }

        const book = bookDetail();
        return HttpResponse.json({
          ...book,
          papers: book.papers.map((p) => (p.uid === params.paperUid ? { ...p, title: body.title, titleSource: 'Manual' } : p)),
        });
      }),
    );
    const { user } = renderEdit(bookDetail());

    const edit = await screen.findByRole('button', { name: '01_Bildiri.docx başlığını düzenle' });
    await user.click(edit);
    const box = screen.getByRole('textbox', { name: '01_Bildiri.docx başlığı' });
    expect(box).toHaveValue('BİLDİRİ 01 BAŞLIĞI');
    expect(box).toHaveFocus();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('textbox', { name: '01_Bildiri.docx başlığı' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '01_Bildiri.docx başlığını düzenle' })).toHaveFocus();
    expect(bodies).toEqual([]);

    await user.click(screen.getByRole('button', { name: '01_Bildiri.docx başlığını düzenle' }));
    await user.clear(screen.getByRole('textbox', { name: '01_Bildiri.docx başlığı' }));
    await user.keyboard('Başlık ad@example.org{Enter}');
    expect(await screen.findByText('Başlıkta e-posta adresi veya telefon numarası bulunamaz.')).toBeInTheDocument();
    const again = screen.getByRole('textbox', { name: '01_Bildiri.docx başlığı' });
    expect(again).toHaveValue('Başlık ad@example.org');

    await user.clear(again);
    await user.keyboard('  Düzeltilmiş Başlık {Enter}');
    expect(await screen.findByText('Düzeltilmiş Başlık')).toBeInTheDocument();
    expect(bodies.at(-1)).toEqual({ paperUid: bookDetail().papers[0]!.uid, title: 'Düzeltilmiş Başlık' });
    expect(screen.getByRole('button', { name: '01_Bildiri.docx başlığını düzenle' })).toHaveFocus();
    // A title the user typed needs no "check it" note.
    expect(screen.queryByText(/kontrol edin/i)).not.toBeInTheDocument();
  });

  it('refuses an empty title without a request', async () => {
    const { user } = renderEdit(bookDetail());
    await user.click(await screen.findByRole('button', { name: '02_Bildiri.docx başlığını düzenle' }));
    await user.clear(screen.getByRole('textbox', { name: '02_Bildiri.docx başlığı' }));
    await user.click(screen.getByRole('button', { name: 'Kaydet' }));

    expect(screen.getByRole('textbox', { name: '02_Bildiri.docx başlığı' })).toHaveAccessibleDescription(
      'Bildiri başlığı boş olamaz.',
    );
  });

  it('reorders the papers with the same buttons as the order step', async () => {
    let body: unknown;
    server.use(
      http.put('/api/books/:uid/paper-order', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(bookDetail());
      }),
    );
    const { user } = renderEdit(bookDetail());
    const [a, b, c] = bookDetail().papers;

    await user.click(await screen.findByRole('button', { name: '01_Bildiri.docx dosyasını aşağı taşı' }));

    await waitFor(() => {
      expect(body).toEqual({ paperUids: [b!.uid, a!.uid, c!.uid] });
    });
  });

  it('asks once before the first change of a completed book and shows the book link while nothing changed', async () => {
    let requests = 0;
    server.use(
      http.put('/api/books/:uid', async ({ request }) => {
        requests++;
        const { name } = (await request.json()) as { name: string };
        return HttpResponse.json(bookDetail({ name, status: 'Uploaded' }));
      }),
    );
    const { user, setCurrent } = renderEdit(completed());

    expect(await screen.findByText(COMPLETED_NOTICE)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Kitabı görüntüle' })).toHaveAttribute(
      'href',
      '/kitaplar/b0000000-0000-0000-0000-000000000001',
    );
    const field = screen.getByLabelText('Kitap adı');
    await user.clear(field);
    await user.type(field, 'Yeni Ad');
    await user.click(screen.getByRole('button', { name: 'Adı kaydet' }));

    const dialog = screen.getByRole('dialog', { name: 'PDF silinsin mi?' });
    expect(dialog).toHaveAccessibleDescription(COMPLETED_NOTICE);
    await user.click(within(dialog).getByRole('button', { name: 'Vazgeç' }));
    expect(requests).toBe(0);
    expect(field).toHaveValue('Yeni Ad');

    await user.click(screen.getByRole('button', { name: 'Adı kaydet' }));
    setCurrent(bookDetail({ name: 'Yeni Ad', status: 'Uploaded' }));
    await user.click(within(screen.getByRole('dialog', { name: 'PDF silinsin mi?' })).getByRole('button', { name: 'Devam et' }));
    await waitFor(() => {
      expect(requests).toBe(1);
    });

    // Reopened: no more question, and the book can be generated again.
    expect(await screen.findByRole('button', { name: 'Kitabı Oluştur' })).toBeInTheDocument();
    expect(screen.queryByText(COMPLETED_NOTICE)).not.toBeInTheDocument();
    await user.clear(field);
    await user.type(field, 'Daha Yeni Ad');
    await user.click(screen.getByRole('button', { name: 'Adı kaydet' }));
    await waitFor(() => {
      expect(requests).toBe(2);
    });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('explains a 409, reloads the book and keeps what the user typed', async () => {
    server.use(http.put('/api/books/:uid', () => problem(409, 'EDIT_CONFLICT', 'Kitap bu sırada değiştirildi.')));
    const { user, gets } = renderEdit(bookDetail());
    const field = await screen.findByLabelText('Kitap adı');
    const before = gets();

    await user.clear(field);
    await user.type(field, 'Çakışan Ad');
    await user.click(screen.getByRole('button', { name: 'Adı kaydet' }));

    // In the notice at the top, under the field and read out.
    expect(await screen.findAllByText(CONFLICT_MESSAGE)).toHaveLength(3);
    expect(field).toHaveAccessibleDescription(expect.stringContaining(CONFLICT_MESSAGE));
    await waitFor(() => {
      expect(gets()).toBeGreaterThan(before);
    });
    expect(field).toHaveValue('Çakışan Ad');
  });

  it('turns the fields off while the book is being generated and links to its page', async () => {
    renderEdit(bookDetail({ status: 'Processing', stage: 'Rendering', progressPercent: 40 }));

    expect(await screen.findByText('Kitap oluşturulurken düzenlenemez.')).toBeInTheDocument();
    expect(screen.getByLabelText('Kitap adı')).toBeDisabled();
    expect(screen.getByRole('button', { name: '01_Bildiri.docx başlığını düzenle' })).toBeDisabled();
    expect(screen.getByRole('button', { name: '01_Bildiri.docx dosyasını aşağı taşı' })).toBeDisabled();
    expect(screen.getByRole('link', { name: 'Kitap sayfasına git' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Kitabı Oluştur' })).not.toBeInTheDocument();
  });

  it('starts generation and goes to the book page', async () => {
    let started = false;
    server.use(
      http.post('/api/books/:uid/generate', () => {
        started = true;
        return new HttpResponse(null, { status: 202 });
      }),
    );
    const { user } = renderEdit(bookDetail());

    await user.click(await screen.findByRole('button', { name: 'Kitabı Oluştur' }));

    expect(await screen.findByText('Başka sayfa')).toBeInTheDocument();
    expect(started).toBe(true);
  });

  it('deletes the book after the same confirmation as Kitaplarım and goes back to the list', async () => {
    let deleted = false;
    server.use(
      http.delete('/api/books/:uid', () => {
        deleted = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = renderEdit(bookDetail());

    await user.click(await screen.findByRole('button', { name: 'Kitabı sil' }));
    const dialog = screen.getByRole('dialog', { name: 'Kitabı sil' });
    expect(dialog).toHaveTextContent('Kitap Silinenler’e taşınacak. Oradan geri alabilirsiniz.');
    await user.click(within(dialog).getByRole('button', { name: 'Sil' }));

    expect(await screen.findByText('Başka sayfa')).toBeInTheDocument();
    expect(deleted).toBe(true);
  });
});
