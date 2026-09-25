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

  it('sorts the list by file name', async () => {
    const { user, input, rows } = setup();
    await user.upload(
      input,
      ['10_Son.docx', '02_Orta.docx', '01_Ilk.docx'].map((name) => docx(name)),
    );

    await user.click(screen.getByRole('button', { name: 'Ada göre sırala' }));

    expect(rows().map((row) => within(row).getByText(/\.docx$/).textContent)).toEqual([
      '01_Ilk.docx',
      '02_Orta.docx',
      '10_Son.docx',
    ]);
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
