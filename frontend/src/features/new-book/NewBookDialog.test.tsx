import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { StrictMode, useState } from 'react';
import { ApiError } from '../../api/errors';
import { uploadBook } from '../../api/upload';
import { bookDetail, docx } from '../../test/fixtures';
import { renderPage } from '../../test/render';
import { DISCARD_QUESTION, MIN_STATUS_MS, NewBookDialog } from './NewBookDialog';

// The upload is a single XMLHttpRequest (tested in api/upload.test.ts); jsdom cannot send File parts through it.
vi.mock('../../api/upload', () => ({ uploadBook: vi.fn() }));
const upload = vi.mocked(uploadBook);

const names = (count: number) =>
  Array.from({ length: count }, (_, i) => `${String(i + 1).padStart(2, '0')}_Bildiri.docx`);

/** Kitaplarım's button and the modal it opens. */
function Harness() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button
        type="button"
        onClick={() => {
          setOpen(true);
        }}
      >
        Yeni kitap
      </button>
      <NewBookDialog
        open={open}
        onClose={() => {
          setOpen(false);
        }}
      />
    </>
  );
}

async function openDialog({ strict = false } = {}) {
  // StrictMode runs every effect twice when it mounts, as the development server (npm run dev) does.
  const view = renderPage(
    strict ? (
      <StrictMode>
        <Harness />
      </StrictMode>
    ) : (
      <Harness />
    ),
  );
  await view.user.click(screen.getByRole('button', { name: 'Yeni kitap' }));
  return view;
}

async function setup() {
  const view = await openDialog();
  const input = screen.getByLabelText('Bildiri dosyaları');
  const submit = () => screen.getByRole('button', { name: /Yükle ve devam et|Yükleniyor|Kontrol ediliyor/ });
  const rows = () => within(screen.getByRole('list', { name: 'Seçilen dosyalar' })).getAllByRole('listitem');
  return { ...view, input, submit, rows };
}

const dialog = () => screen.getByRole('dialog', { name: 'Yeni kitap' });
const question = () => screen.queryByRole('dialog', { name: 'Yeni kitap kapatılsın mı?' });

/** What the browser does on Esc: a cancelable "cancel" event on the topmost open dialog. */
function pressEscape(target: HTMLElement) {
  act(() => {
    fireEvent(target, new Event('cancel', { cancelable: true }));
  });
}

async function waitUntilChecked() {
  await waitFor(() => {
    expect(screen.queryByText('Denetleniyor')).not.toBeInTheDocument();
  });
}

describe('NewBookDialog', () => {
  it('opens under StrictMode without the name error, with one showModal, the right focus and the page locked', async () => {
    const showModal = vi.spyOn(HTMLDialogElement.prototype, 'showModal');
    document.documentElement.style.overflow = 'auto';
    try {
      const { user } = await openDialog({ strict: true });

      expect(screen.queryByText('Kitap adını yazın.')).not.toBeInTheDocument();
      expect(screen.getByLabelText('Kitap adı')).toHaveFocus();
      expect(showModal).toHaveBeenCalledTimes(1);
      expect(dialog()).toHaveAttribute('open');
      expect(document.documentElement.style.overflow).toBe('hidden');

      await user.click(screen.getByRole('button', { name: 'Vazgeç' }));

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Yeni kitap' })).toHaveFocus();
      expect(document.documentElement.style.overflow).toBe('auto');
      expect(screen.queryByText('Kitap adını yazın.')).not.toBeInTheDocument();
    } finally {
      showModal.mockRestore();
      document.documentElement.style.overflow = '';
    }
  });

  it('shows the name error once the user typed, cleared the field and left it', async () => {
    const { user } = await openDialog({ strict: true });
    const field = screen.getByLabelText('Kitap adı');

    // Only focus moving through the field: no message.
    await user.tab();
    expect(screen.queryByText('Kitap adını yazın.')).not.toBeInTheDocument();

    await user.click(field);
    await user.type(field, 'Ki');
    await user.clear(field);
    expect(screen.queryByText('Kitap adını yazın.')).not.toBeInTheDocument();
    await user.tab();

    expect(field).toHaveAccessibleDescription(expect.stringContaining('Kitap adını yazın.'));
  });

  it('shows the name error when the form is sent with an empty name', async () => {
    await openDialog({ strict: true });
    expect(screen.queryByText('Kitap adını yazın.')).not.toBeInTheDocument();

    // "Yükle ve devam et" stays disabled until the form is complete; the submit itself is what counts.
    act(() => {
      fireEvent.submit(screen.getByLabelText('Kitap adı').closest('form')!);
    });

    expect(screen.getByLabelText('Kitap adı')).toHaveAccessibleDescription(
      expect.stringContaining('Kitap adını yazın.'),
    );
    expect(upload).not.toHaveBeenCalled();
  });

  it('opens with the focus on the book name, is labelled by its title and returns the focus on close', async () => {
    const { user } = await openDialog();

    expect(dialog()).toHaveAttribute('aria-labelledby');
    expect(screen.getByLabelText('Kitap adı')).toHaveFocus();
    // No stepper in the modal.
    expect(within(dialog()).queryByText('Sıra ve kontrol')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Vazgeç' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Yeni kitap' })).toHaveFocus();
  });

  it('has an "X" button that follows the rules of "Vazgeç"', async () => {
    const { user } = await openDialog();
    const close = () => within(dialog()).getByRole('button', { name: 'Kapat' });

    // Nothing entered: closes at once.
    await user.click(close());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Yeni kitap' })).toHaveFocus();

    // A typed name: asks first.
    await user.click(screen.getByRole('button', { name: 'Yeni kitap' }));
    await user.type(screen.getByLabelText('Kitap adı'), 'Yarım Kalan Kitap');
    await user.click(close());
    expect(question()).toHaveAccessibleDescription(DISCARD_QUESTION);
    await user.click(within(question()!).getByRole('button', { name: 'Evet' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('keeps the page behind from scrolling while it is open', async () => {
    document.documentElement.style.overflow = 'auto';
    const { user } = await openDialog();
    expect(document.documentElement.style.overflow).toBe('hidden');

    await user.click(within(dialog()).getByRole('button', { name: 'Kapat' }));

    expect(document.documentElement.style.overflow).toBe('auto');
    document.documentElement.style.overflow = '';
  });

  it('closes at once on Esc or an outside click while nothing was entered', async () => {
    const { user } = await openDialog();
    pressEscape(dialog());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Yeni kitap' }));
    // A click on the <dialog> element itself is a click on its backdrop.
    await user.click(dialog());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('asks before throwing away a typed name or chosen files', async () => {
    const { user, input } = await setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Yarım Kalan Kitap');

    await user.click(screen.getByRole('button', { name: 'Vazgeç' }));
    expect(question()).toHaveAccessibleDescription(DISCARD_QUESTION);
    expect(within(question()!).getByRole('button', { name: 'Hayır' })).toHaveFocus();

    await user.click(within(question()!).getByRole('button', { name: 'Hayır' }));
    expect(question()).not.toBeInTheDocument();
    expect(screen.getByLabelText('Kitap adı')).toHaveValue('Yarım Kalan Kitap');

    await user.clear(screen.getByLabelText('Kitap adı'));
    await user.upload(input, [docx('01_Bildiri.docx')]);
    pressEscape(dialog());
    // Esc on the question closes only the question.
    pressEscape(question()!);
    expect(question()).not.toBeInTheDocument();
    expect(dialog()).toBeInTheDocument();

    await user.click(dialog());
    await user.click(within(question()!).getByRole('button', { name: 'Evet' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Yeni kitap' })).toHaveFocus();

    await user.click(screen.getByRole('button', { name: 'Yeni kitap' }));
    expect(screen.getByLabelText('Kitap adı')).toHaveValue('');
    expect(screen.queryByRole('list', { name: 'Seçilen dosyalar' })).not.toBeInTheDocument();
  });

  it('cannot be closed while the upload runs', async () => {
    upload.mockImplementation(() => new Promise(() => undefined));
    const { user, input, submit } = await setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');
    await user.upload(
      input,
      names(10).map((name) => docx(name)),
    );
    await waitUntilChecked();

    await user.click(submit());

    expect(await screen.findByRole('progressbar', { name: 'Yükleme ilerlemesi' })).toBeInTheDocument();
    expect(within(dialog()).getByRole('progressbar')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Vazgeç' })).toBeDisabled();
    expect(within(dialog()).getByRole('button', { name: 'Kapat' })).toBeDisabled();
    pressEscape(dialog());
    await user.click(dialog());
    expect(dialog()).toBeInTheDocument();
    expect(question()).not.toBeInTheDocument();
  });

  it('keeps the button disabled with nine files and enables it with ten valid files', async () => {
    const { user, input, submit } = await setup();
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
    const { user, input, submit, rows } = await setup();
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

  it('marks a renamed file that is not a Word package as faulty at once', async () => {
    const { user, input, rows, submit } = await setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');

    await user.upload(input, [
      ...names(9).map((name) => docx(name)),
      new File(['Bu bir metin dosyası.'], '10_Bildiri.docx', {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      }),
    ]);
    await waitUntilChecked();

    const row = rows()[9]!;
    expect(within(row).getByText('Hatalı')).toBeInTheDocument();
    expect(within(row).getByText('Geçerli bir Word (.docx) dosyası değil.')).toBeInTheDocument();
    expect(submit()).toBeDisabled();
  });

  it('flags the same content chosen twice under another name', async () => {
    const { user, input, rows } = await setup();

    await user.upload(input, [docx('01_Bildiri.docx', 'aynı'), docx('kopya.docx', 'aynı')]);
    await waitUntilChecked();

    expect(within(rows()[1]!).getByText('01_Bildiri.docx ile aynı içeriğe sahip.')).toBeInTheDocument();
  });

  it('removes a file with "Kaldır"', async () => {
    const { user, input, rows } = await setup();
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
    const { user, input, rows } = await setup();
    const ordered = Array.from({ length: 10 }, (_, i) => `${String(i + 1)}_Bildiri.docx`);
    await user.upload(
      input,
      ['10', '3', '1', '7', '2', '9', '4', '6', '8', '5'].map((n) => docx(`${n}_Bildiri.docx`)),
    );
    const sort = screen.getByRole('button', { name: 'Ada göre sırala' });
    expect(sort).not.toHaveAttribute('aria-disabled');

    await user.click(sort);

    expect(rows().map((row) => within(row).getByText(/\.docx$/).textContent)).toEqual(ordered);
    expect(screen.getByText('Dosyalar ada göre sıralandı.')).toBeInTheDocument();
    // The button stays focusable but is no longer available: the list is sorted now.
    expect(sort).toHaveAttribute('aria-disabled', 'true');
    expect(sort).toHaveFocus();
  });

  it('keeps "Ada göre sırala" unavailable and says why when the files are already in order', async () => {
    const { user, input, rows } = await setup();
    await user.upload(
      input,
      names(3).map((name) => docx(name)),
    );

    const sort = screen.getByRole('button', { name: 'Ada göre sırala' });
    expect(sort).toHaveAttribute('aria-disabled', 'true');
    expect(sort).toHaveAccessibleDescription('Dosyalar zaten ada göre sıralı');
    // The reason is a tooltip, not a line of text next to the button.
    expect(screen.queryByText('Dosyalar zaten ada göre sıralı')).not.toBeVisible();
    await user.click(sort);
    expect(rows()).toHaveLength(3);
    expect(screen.queryByText('Dosyalar ada göre sıralandı.')).not.toBeInTheDocument();
  });

  it('shows the "already sorted" tooltip on hover and closes it the moment the pointer leaves', async () => {
    const { user, input } = await setup();
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
    const { user, input } = await setup();
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
    const { user, input } = await setup();
    await user.upload(input, [docx('2_Bildiri.docx'), docx('1_Bildiri.docx')]);
    const sort = screen.getByRole('button', { name: 'Ada göre sırala' });

    await user.hover(sort);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    expect(sort).not.toHaveAttribute('aria-describedby');
    sort.focus();
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('shows the upload in the body, in place of the form, and keeps what was entered', async () => {
    upload.mockImplementation((_name, _files, onProgress) => {
      onProgress(40);
      return new Promise(() => undefined);
    });
    const { user, input, submit } = await setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');
    await user.upload(
      input,
      names(10).map((name) => docx(name)),
    );
    await waitUntilChecked();

    await user.click(submit());

    const body = dialog().querySelector('[data-dialog-body]')!;
    expect(within(body as HTMLElement).getByRole('status')).toHaveTextContent('Dosyalar yükleniyor… %40');
    expect(
      within(body as HTMLElement).getByRole('progressbar', { name: 'Yükleme ilerlemesi' }),
    ).toHaveAttribute('aria-valuenow', '40');
    // The form is only hidden, so the name and the files are still there if the upload fails.
    expect(screen.getByLabelText('Kitap adı')).not.toBeVisible();
    expect(screen.getByLabelText('Kitap adı')).toHaveValue('Örnek Bilim Kongresi 2026');
    expect(screen.getByRole('button', { name: 'Yükleniyor' })).toBeDisabled();
  });

  it('keeps the status panel on screen for at least 600 ms before the book page opens', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      let answer: (book: ReturnType<typeof bookDetail>) => void = () => undefined;
      upload.mockImplementation((_name, _files, onProgress) => {
        onProgress(100);
        return new Promise((resolve) => {
          answer = resolve;
        });
      });
      const { user, input, submit } = await setup();
      await user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');
      await user.upload(
        input,
        names(10).map((name) => docx(name)),
      );
      await waitUntilChecked();

      vi.setSystemTime(new Date(2026, 8, 29, 7, 0, 0, 0));
      await user.click(submit());
      expect(screen.getByRole('status')).toHaveTextContent(
        'Dosyalar kontrol ediliyor ve başlıklar tespit ediliyor…',
      );

      // The server answers after 100 ms: the panel stays for the rest of the 600 ms.
      vi.setSystemTime(new Date(2026, 8, 29, 7, 0, 0, 100));
      await act(async () => {
        answer(bookDetail());
        await Promise.resolve();
      });
      await act(async () => {
        await vi.advanceTimersByTimeAsync(MIN_STATUS_MS - 150);
      });
      expect(screen.getByRole('dialog', { name: 'Yeni kitap' })).toBeInTheDocument();
      expect(screen.queryByText('Başka sayfa')).not.toBeInTheDocument();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(100);
      });
      expect(screen.getByText('Başka sayfa')).toBeInTheDocument();
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows the server check as a second stage once the upload reached 100 %', async () => {
    upload.mockImplementation((_name, _files, onProgress) => {
      onProgress(100);
      return new Promise(() => undefined);
    });
    const { user, input } = await setup();
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

  it('uploads in list order, closes and opens the book page', async () => {
    upload.mockImplementation((_name, _files, onProgress) => {
      onProgress(50);
      return Promise.resolve(bookDetail());
    });
    const { user, input, submit } = await setup();
    await user.type(screen.getByLabelText('Kitap adı'), '  Örnek Bilim Kongresi 2026 ');
    await user.upload(
      input,
      names(10).map((name) => docx(name)),
    );
    await waitUntilChecked();

    await user.click(submit());

    expect(await screen.findByText('Başka sayfa')).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    const [name, files] = upload.mock.calls[0]!;
    expect(name).toBe('Örnek Bilim Kongresi 2026');
    expect(files.map((file) => file.name)).toEqual(names(10));
  });

  describe('server errors on file rows', () => {
    const notDocx = (name: string) => ({
      code: 'FILE_NOT_DOCX',
      message: `${name} geçerli bir Word (.docx) belgesi değil.`,
      field: null,
      fileName: name,
    });

    /** Ten files sent; the server rejects the 3rd, 5th and 7th, and optionally the name and the whole upload. */
    async function rejectedUpload({ withName = false, withGeneral = false } = {}) {
      upload.mockRejectedValue(
        new ApiError(400, {
          type: null,
          title: 'Yükleme doğrulanamadı.',
          status: 400,
          detail: 'Yüklemede sorunlar bulundu.',
          code: 'VALIDATION_FAILED',
          traceId: 't',
          errors: [
            notDocx('03_Bildiri.docx'),
            notDocx('05_Bildiri.docx'),
            notDocx('07_Bildiri.docx'),
            ...(withName
              ? [{ code: 'BOOK_NAME_INVALID', message: 'Ad geçersiz.', field: 'name', fileName: null }]
              : []),
            ...(withGeneral
              ? [{ code: 'TOTAL_SIZE_TOO_LARGE', message: 'Toplam fazla.', field: null, fileName: null }]
              : []),
          ],
        }),
      );
      const view = await setup();
      await view.user.type(screen.getByLabelText('Kitap adı'), 'Örnek Bilim Kongresi 2026');
      await view.user.upload(
        view.input,
        names(10).map((name) => docx(name)),
      );
      await waitUntilChecked();
      await view.user.click(view.submit());
      expect(await screen.findAllByText('Reddedildi')).toHaveLength(3);
      return view;
    }

    const rejectedFiles = (rows: HTMLElement[]) =>
      rows
        .filter((row) => within(row).queryByText('Reddedildi'))
        .map((row) => within(row).getByText(/\.docx$/).textContent);

    it('keeps the other rejected rows when one of them is removed', async () => {
      const { user, rows, submit } = await rejectedUpload();
      expect(submit()).toBeDisabled();

      await user.click(screen.getByRole('button', { name: '05_Bildiri.docx dosyasını kaldır' }));

      expect(rejectedFiles(rows())).toEqual(['03_Bildiri.docx', '07_Bildiri.docx']);
      expect(within(rows()[2]!).getByText('Geçerli bir Word (.docx) belgesi değil.')).toBeInTheDocument();
      // Still rejected rows: the same upload cannot be sent again.
      expect(submit()).toBeDisabled();
    });

    it('keeps the rejected rows when a file is added or the list is sorted; a general message goes', async () => {
      const { user, input, rows } = await rejectedUpload({ withGeneral: true });
      expect(screen.getByRole('alert')).toHaveTextContent('Dosyaların toplam boyutu 60 MB sınırını aşıyor.');

      await user.click(screen.getByRole('button', { name: '05_Bildiri.docx dosyasını kaldır' }));
      await user.upload(input, [docx('00_Yeni.docx')]);
      await waitUntilChecked();
      expect(rejectedFiles(rows())).toEqual(['03_Bildiri.docx', '07_Bildiri.docx']);
      expect(screen.queryByText('Dosyaların toplam boyutu 60 MB sınırını aşıyor.')).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Ada göre sırala' }));
      expect(rows().map((row) => within(row).getByText(/\.docx$/).textContent)[0]).toBe('00_Yeni.docx');
      expect(rejectedFiles(rows())).toEqual(['03_Bildiri.docx', '07_Bildiri.docx']);
    });

    it('does not pass a message on to a new file with the same name', async () => {
      const { user, input, rows } = await rejectedUpload();

      await user.click(screen.getByRole('button', { name: '03_Bildiri.docx dosyasını kaldır' }));
      await user.upload(input, [docx('03_Bildiri.docx', 'düzeltilmiş içerik')]);
      await waitUntilChecked();

      const again = rows().find((row) => within(row).queryByText('03_Bildiri.docx'))!;
      expect(within(again).getByText('Uygun')).toBeInTheDocument();
      expect(rejectedFiles(rows())).toEqual(['05_Bildiri.docx', '07_Bildiri.docx']);
    });

    it('clears only the name message when the name changes', async () => {
      const { user, rows } = await rejectedUpload({ withName: true });
      const field = screen.getByLabelText('Kitap adı');
      expect(field).toHaveAccessibleDescription(expect.stringContaining('Kitap adı 3–150 karakter olmalı'));

      await user.type(field, ' 2');

      expect(field).not.toHaveAccessibleDescription(
        expect.stringContaining('Kitap adı 3–150 karakter olmalı'),
      );
      expect(rejectedFiles(rows())).toEqual(['03_Bildiri.docx', '05_Bildiri.docx', '07_Bildiri.docx']);
    });

    it('allows the upload again once every rejected file is removed and ten files are chosen', async () => {
      const { user, input, submit } = await rejectedUpload();

      for (const name of ['03_Bildiri.docx', '05_Bildiri.docx', '07_Bildiri.docx']) {
        await user.click(screen.getByRole('button', { name: `${name} dosyasını kaldır` }));
      }
      await user.upload(input, [docx('A.docx'), docx('B.docx'), docx('C.docx')]);
      await waitUntilChecked();

      expect(screen.queryByText('Reddedildi')).not.toBeInTheDocument();
      expect(submit()).toBeEnabled();
    });
  });

  it('shows server errors inside the modal, on the file row and under the name field', async () => {
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
    const { user, input, submit, rows } = await setup();
    await user.type(screen.getByLabelText('Kitap adı'), 'Kongre 😀');
    await user.upload(
      input,
      names(10).map((name) => docx(name)),
    );
    await waitUntilChecked();

    await user.click(submit());

    // The status panel steps aside and the form comes back with the messages.
    expect(await screen.findByText('Geçerli bir Word (.docx) belgesi değil.')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Kitap adı')).toHaveValue('Kongre 😀');
    const row = rows()[2]!;
    expect(within(row).getByText('Geçerli bir Word (.docx) belgesi değil.')).toBeInTheDocument();
    expect(within(row).getByText('Reddedildi')).toBeInTheDocument();
    expect(dialog()).toContainElement(row);
    expect(screen.getByLabelText('Kitap adı')).toHaveAccessibleDescription(
      expect.stringContaining("Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor"),
    );
  });

  it('explains a network failure', async () => {
    upload.mockRejectedValue(new ApiError(0));
    const { user, input, submit } = await setup();
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
