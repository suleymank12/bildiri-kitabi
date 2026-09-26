import { screen, waitFor, within } from '@testing-library/react';
import { ApiError } from '../../api/errors';
import { uploadBook } from '../../api/upload';
import { bookDetail, docx } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { NewBookPage } from './NewBookPage';

// The upload is a single XMLHttpRequest (tested in api/upload.test.ts); jsdom cannot send File parts through it.
vi.mock('../../api/upload', () => ({ uploadBook: vi.fn() }));
const upload = vi.mocked(uploadBook);

const names = (count: number) =>
  Array.from({ length: count }, (_, i) => `${String(i + 1).padStart(2, '0')}_Bildiri.docx`);

function setup() {
  const view = renderPage(<NewBookPage />);
  const input = screen.getByLabelText('Bildiri dosyaları');
  const submit = () => screen.getByRole('button', { name: /Yükle ve devam et|Yükleniyor/ });
  const rows = () => within(screen.getByRole('list', { name: 'Seçilen dosyalar' })).getAllByRole('listitem');
  return { ...view, input, submit, rows };
}

async function waitUntilChecked() {
  await waitFor(() => {
    expect(screen.queryByText('Denetleniyor')).not.toBeInTheDocument();
  });
}

describe('NewBookPage', () => {
  it('keeps the button disabled with nine files and enables it with ten valid files', async () => {
    const { user, input, submit } = setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');

    await user.upload(
      input,
      names(9).map((name) => docx(name)),
    );
    await waitUntilChecked();
    expect(screen.getByText(/seçildi/)).toHaveTextContent("10 dosyadan 9'u seçildi");
    expect(submit()).toBeDisabled();

    await user.upload(input, [docx('10_Bildiri.docx')]);
    await waitUntilChecked();
    expect(screen.getByText(/seçildi/)).toHaveTextContent("10 dosyadan 10'u seçildi");
    expect(submit()).toBeEnabled();
  });

  it('marks a .pdf on its row and keeps the button disabled', async () => {
    const { user, input, submit, rows } = setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');

    await user.upload(input, [
      ...names(9).map((name) => docx(name)),
      new File(['%PDF'], 'makale.pdf', { type: 'application/pdf' }),
    ]);
    await waitUntilChecked();

    const pdfRow = rows()[9]!;
    expect(within(pdfRow).getByText('makale.pdf')).toBeInTheDocument();
    expect(within(pdfRow).getByText('Hatalı')).toBeInTheDocument();
    expect(
      within(pdfRow).getByText('Yalnızca .docx uzantılı Word belgeleri yüklenebilir.'),
    ).toBeInTheDocument();
    expect(submit()).toBeDisabled();
  });

  it('flags the same content chosen twice under another name', async () => {
    const { user, input, rows } = setup();

    await user.upload(input, [docx('01_Bildiri.docx', 'aynı'), docx('kopya.docx', 'aynı')]);
    await waitUntilChecked();

    expect(within(rows()[1]!).getByText('01_Bildiri.docx ile aynı içeriğe sahip.')).toBeInTheDocument();
  });

  it('removes a file with "Kaldır"', async () => {
    const { user, input, rows } = setup();
    await user.upload(
      input,
      names(3).map((name) => docx(name)),
    );

    await user.click(screen.getByRole('button', { name: '02_Bildiri.docx dosyasını kaldır' }));

    expect(rows().map((row) => within(row).getByText(/\.docx$/).textContent)).toEqual([
      '01_Bildiri.docx',
      '03_Bildiri.docx',
    ]);
    expect(screen.getByText(/seçildi/)).toHaveTextContent("10 dosyadan 2'si seçildi");
  });

  it('sorts ten unordered files in natural name order and confirms it', async () => {
    const { user, input, rows } = setup();
    const ordered = Array.from({ length: 10 }, (_, i) => `${String(i + 1)}_Bildiri.docx`);
    await user.upload(
      input,
      ['10', '3', '1', '7', '2', '9', '4', '6', '8', '5'].map((n) => docx(`${n}_Bildiri.docx`)),
    );
    const sort = screen.getByRole('button', { name: 'Ada göre sırala' });
    expect(sort).not.toHaveAttribute('aria-disabled');

    await user.click(sort);

    expect(rows().map((row) => within(row).getByText(/\.docx$/).textContent)).toEqual(ordered);
    expect(screen.getByRole('status')).toHaveTextContent('Dosyalar ada göre sıralandı.');
    // The button stays focusable but is no longer available: the list is sorted now.
    expect(sort).toHaveAttribute('aria-disabled', 'true');
    expect(sort).toHaveFocus();
  });

  it('keeps "Ada göre sırala" unavailable and says why when the files are already in order', async () => {
    const { user, input, rows } = setup();
    await user.upload(
      input,
      names(3).map((name) => docx(name)),
    );

    const sort = screen.getByRole('button', { name: 'Ada göre sırala' });
    expect(sort).toHaveAttribute('aria-disabled', 'true');
    expect(sort).toHaveAccessibleDescription('Dosyalar zaten ada göre sıralı');
    // The reason is a tooltip now, not a line of text next to the button.
    expect(screen.queryByText('Dosyalar zaten ada göre sıralı')).not.toBeVisible();
    await user.click(sort);
    expect(rows()).toHaveLength(3);
    expect(screen.getByRole('status')).toHaveTextContent('');
  });

  it('shows the "already sorted" tooltip on hover and closes it the moment the pointer leaves', async () => {
    const { user, input } = setup();
    await user.upload(
      input,
      names(3).map((name) => docx(name)),
    );
    const sort = screen.getByRole('button', { name: 'Ada göre sırala' });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();

    await user.hover(sort);
    expect(screen.getByRole('tooltip')).toHaveTextContent('Dosyalar zaten ada göre sıralı');
    expect(sort).toHaveAttribute('aria-describedby', screen.getByRole('tooltip').id);

    await user.unhover(sort);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('shows the "already sorted" tooltip on keyboard focus and closes it with Esc', async () => {
    const { user, input } = setup();
    await user.upload(
      input,
      names(3).map((name) => docx(name)),
    );
    const sort = screen.getByRole('button', { name: 'Ada göre sırala' });

    // The file input sits right before the button in the tab order.
    await user.click(screen.getByLabelText('Bildiri dosyaları'));
    await user.tab();
    expect(sort).toHaveFocus();
    expect(screen.getByRole('tooltip')).toHaveTextContent('Dosyalar zaten ada göre sıralı');

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    expect(sort).toHaveFocus();
  });

  it('has no tooltip while the files are not in order', async () => {
    const { user, input } = setup();
    await user.upload(input, [docx('2_Bildiri.docx'), docx('1_Bildiri.docx')]);
    const sort = screen.getByRole('button', { name: 'Ada göre sırala' });

    await user.hover(sort);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    expect(sort).not.toHaveAttribute('aria-describedby');
    sort.focus();
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('shows the server check as a second stage once the upload reached 100 %', async () => {
    upload.mockImplementation((_name, _files, onProgress) => {
      onProgress(100);
      return new Promise(() => undefined);
    });
    const { user, input } = setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');
    await user.upload(
      input,
      names(10).map((name) => docx(name)),
    );
    await waitUntilChecked();

    await user.click(screen.getByRole('button', { name: 'Yükle ve devam et' }));

    expect(
      await screen.findByText('Dosyalar kontrol ediliyor ve başlıklar tespit ediliyor…'),
    ).toBeInTheDocument();
    const bar = screen.getByRole('progressbar', { name: 'Dosyalar kontrol ediliyor' });
    expect(bar).not.toHaveAttribute('aria-valuenow');
    expect(screen.getByRole('button', { name: 'Kontrol ediliyor' })).toBeDisabled();
  });

  it('uploads in list order and opens the book page', async () => {
    upload.mockImplementation((_name, _files, onProgress) => {
      onProgress(50);
      return Promise.resolve(bookDetail());
    });
    const { user, input, submit } = setup();
    await user.type(screen.getByLabelText('Kitap adı'), '  Örnek Bilim Kongresi 2026 ');
    await user.upload(
      input,
      names(10).map((name) => docx(name)),
    );
    await waitUntilChecked();

    await user.click(submit());

    expect(await screen.findByText('Başka sayfa')).toBeInTheDocument();
    const [name, files] = upload.mock.calls[0]!;
    expect(name).toBe('Örnek Bilim Kongresi 2026');
    expect(files.map((file) => file.name)).toEqual(names(10));
  });

  it('shows server errors on the file row and under the name field', async () => {
    upload.mockRejectedValue(
      new ApiError(400, {
        type: null,
        title: 'Yükleme doğrulanamadı.',
        status: 400,
        detail: 'Yüklemede 2 sorun bulundu.',
        code: 'VALIDATION_FAILED',
        traceId: 't',
        errors: [
          {
            code: 'FILE_NOT_DOCX',
            message: '03_Bildiri.docx geçerli bir Word (.docx) belgesi değil.',
            field: null,
            fileName: '03_Bildiri.docx',
          },
          {
            code: 'BOOK_NAME_UNSUPPORTED_CHARACTER',
            message: "Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.",
            field: 'name',
            fileName: null,
          },
        ],
      }),
    );
    const { user, input, submit, rows } = setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Kongre 😀');
    await user.upload(
      input,
      names(10).map((name) => docx(name)),
    );
    await waitUntilChecked();

    await user.click(submit());

    expect(
      await within(rows()[2]!).findByText('Geçerli bir Word (.docx) belgesi değil.'),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Kitap adı')).toHaveAccessibleDescription(
      expect.stringContaining("Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor"),
    );
  });

  it('explains a network failure', async () => {
    upload.mockRejectedValue(new ApiError(0));
    const { user, input, submit } = setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');
    await user.upload(
      input,
      names(10).map((name) => docx(name)),
    );
    await waitUntilChecked();

    await user.click(submit());

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Sunucuya ulaşılamıyor. Bağlantınızı kontrol edip tekrar deneyin.',
    );
  });
});
