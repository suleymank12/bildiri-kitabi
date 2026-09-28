import { screen, within } from '@testing-library/react';
import { useEffect, type ReactNode } from 'react';
import type { BookDetail } from '../../api/types';
import { bookDetail, paper } from '../../test/fixtures';
import { setViewportWidth } from '../../test/media';
import { renderPage } from '../../test/render';
import { PdfViewer } from './PdfViewer';

// react-pdf needs a real canvas and the pdf.js worker; the tests replace it with stand-ins that report a
// 22-page A4 document (or a load error) and render each page as a marker, so the viewer's own logic is tested.
const pdfState = vi.hoisted(() => ({ fail: false }));

vi.mock('react-pdf', () => ({
  pdfjs: { GlobalWorkerOptions: {} },
  Document: ({
    children,
    onLoadSuccess,
    onLoadError,
  }: {
    children: ReactNode;
    onLoadSuccess?: (document: unknown) => void;
    onLoadError?: (error: Error) => void;
  }) => {
    useEffect(() => {
      if (pdfState.fail) {
        onLoadError?.(new Error('yüklenemedi'));
      } else {
        onLoadSuccess?.({
          numPages: 22,
          getPage: () => Promise.resolve({ getViewport: () => ({ width: 595, height: 842 }) }),
        });
      }
      // Runs once, like a document load.
      // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);
    return <div>{children}</div>;
  },
  Page: ({ pageNumber }: { pageNumber: number }) => <div data-testid="pdf-page">{pageNumber}</div>,
}));

const completed: BookDetail = bookDetail({
  uid: 'kitap-1',
  status: 'Completed',
  pageCount: 22,
  pdfUrl: '/api/books/kitap-1/pdf',
  papers: Array.from({ length: 10 }, (_, i) =>
    paper(i + 1, { startPage: 3 + 2 * i, endPage: 4 + 2 * i, title: `BİLDİRİ ${String(i + 1)}` }),
  ),
});

function renderViewer(query = '') {
  return renderPage(<PdfViewer book={completed} />, {
    path: '/kitaplar/:uid',
    route: `/kitaplar/kitap-1${query}`,
  });
}

/** Page numbers on screen, left to right (the prefetched next spread is not counted). */
function visibleSlots(): string[] {
  return Array.from(document.querySelectorAll('[data-spread] [data-page-slot]')).map(
    (element) => element.getAttribute('data-page-slot') ?? '',
  );
}

const pageBox = () => screen.getByLabelText('Sayfa numarası');

afterEach(() => {
  pdfState.fail = false;
});

describe('PdfViewer on a wide screen', () => {
  it('opens the page from ?sayfa= in facing pages', async () => {
    renderViewer('?sayfa=3');

    expect(await screen.findByDisplayValue('3')).toBe(pageBox());
    expect(visibleSlots()).toEqual(['2', '3']);
    expect(screen.getByRole('button', { name: 'Çift sayfa' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('shows the cover alone (centred, without an empty partner) and falls back to it for an invalid page', async () => {
    renderViewer('?sayfa=abc');

    expect(await screen.findByDisplayValue('1')).toBe(pageBox());
    expect(visibleSlots()).toEqual(['1']);
  });

  it('opens at "fit width" and reports the zoom against the real page size', async () => {
    const { user } = renderViewer('?sayfa=3');
    await screen.findByDisplayValue('3');

    expect(screen.getByRole('button', { name: 'Genişliğe sığdır' })).toHaveAttribute('aria-pressed', 'true');
    // jsdom has no layout: the area falls back to 800 px, so each of the two pages is 376 px wide, 47 % of an
    // A4 page at 96 dpi (793 px).
    expect(screen.getByLabelText('Yakınlaştırma yüzdesi')).toHaveValue('%47');

    await user.click(screen.getByRole('button', { name: 'Yakınlaştır' }));
    expect(screen.getByLabelText('Yakınlaştırma yüzdesi')).toHaveValue('%75');
    expect(screen.getByRole('button', { name: 'Genişliğe sığdır' })).toHaveAttribute('aria-pressed', 'false');
    // Zooming keeps the page.
    expect(pageBox()).toHaveValue('3');
    expect(visibleSlots()).toEqual(['2', '3']);
  });

  describe('zoom box', () => {
    const zoomBox = () => screen.getByLabelText('Yakınlaştırma yüzdesi');
    const fitWidth = () => screen.getByRole('button', { name: 'Genişliğe sığdır' });

    async function typeZoom(text: string) {
      const view = renderViewer('?sayfa=3');
      await screen.findByDisplayValue('3');
      await view.user.click(zoomBox());
      await view.user.keyboard(`${text}{Enter}`);
      return view;
    }

    it('selects the value when clicked', async () => {
      const { user } = renderViewer('?sayfa=3');
      await screen.findByDisplayValue('3');

      await user.click(zoomBox());

      const box = zoomBox() as HTMLInputElement;
      expect(box.value).toBe('%47');
      expect([box.selectionStart, box.selectionEnd]).toEqual([0, 3]);
    });

    it.each(['150', '%150', '150%'])('applies "%s" and leaves the fit mode, on the same page', async (text) => {
      await typeZoom(text);

      expect(zoomBox()).toHaveValue('%150');
      expect(fitWidth()).toHaveAttribute('aria-pressed', 'false');
      expect(screen.getByRole('button', { name: 'Sayfaya sığdır' })).toHaveAttribute('aria-pressed', 'false');
      expect(pageBox()).toHaveValue('3');
      expect(visibleSlots()).toEqual(['2', '3']);
    });

    it('clamps values outside 25–400 %', async () => {
      const { user } = await typeZoom('500');
      expect(zoomBox()).toHaveValue('%400');
      expect(screen.getByRole('button', { name: 'Yakınlaştır' })).toBeDisabled();

      // After Enter the box keeps the focus with the value selected, so typing replaces it.
      await user.keyboard('10{Enter}');
      expect(zoomBox()).toHaveValue('%25');
      expect(screen.getByRole('button', { name: 'Uzaklaştır' })).toBeDisabled();
    });

    it('rejects a value that is not a number and keeps the current zoom', async () => {
      await typeZoom('abc');

      expect(zoomBox()).toHaveValue('%47');
      expect(fitWidth()).toHaveAttribute('aria-pressed', 'true');
    });

    it('applies the value when the box is left', async () => {
      const { user } = renderViewer('?sayfa=3');
      await screen.findByDisplayValue('3');

      await user.click(zoomBox());
      await user.keyboard('200');
      await user.tab();

      expect(zoomBox()).toHaveValue('%200');
    });

    it('cancels the typed value with Esc', async () => {
      const { user } = renderViewer('?sayfa=3');
      await screen.findByDisplayValue('3');

      await user.click(zoomBox());
      await user.keyboard('200{Escape}');
      expect(zoomBox()).toHaveValue('%47');

      await user.tab();
      expect(zoomBox()).toHaveValue('%47');
      expect(fitWidth()).toHaveAttribute('aria-pressed', 'true');
    });

    it('steps by 10 points with the up and down arrows', async () => {
      const { user } = renderViewer('?sayfa=3');
      await screen.findByDisplayValue('3');

      await user.click(zoomBox());
      await user.keyboard('{ArrowUp}');
      expect(zoomBox()).toHaveValue('%57');
      expect(fitWidth()).toHaveAttribute('aria-pressed', 'false');

      await user.keyboard('{ArrowDown}{ArrowDown}');
      expect(zoomBox()).toHaveValue('%37');
      expect(pageBox()).toHaveValue('3');
    });
  });

  it('moves spread by spread with the next and previous buttons', async () => {
    const { user } = renderViewer('?sayfa=3');
    await screen.findByDisplayValue('3');

    await user.click(screen.getByRole('button', { name: 'Sonraki sayfa' }));
    expect(visibleSlots()).toEqual(['4', '5']);
    expect(pageBox()).toHaveValue('4');

    await user.click(screen.getByRole('button', { name: 'Önceki sayfa' }));
    await user.click(screen.getByRole('button', { name: 'Önceki sayfa' }));
    expect(visibleSlots()).toEqual(['1']);
    expect(screen.getByRole('button', { name: 'Önceki sayfa' })).toBeDisabled();
  });

  it('goes to the page typed into the page box', async () => {
    const { user } = renderViewer();
    await screen.findByDisplayValue('1');

    await user.clear(pageBox());
    await user.type(pageBox(), '11{Enter}');

    expect(visibleSlots()).toEqual(['10', '11']);
  });

  it('opens a paper from the table of contents and highlights it', async () => {
    const { user } = renderViewer();
    const toc = await screen.findByRole('navigation', { name: 'Bildiriler' });

    await user.click(within(toc).getByRole('button', { name: /BİLDİRİ 5/ }));

    expect(pageBox()).toHaveValue('11');
    expect(visibleSlots()).toEqual(['10', '11']);
    expect(within(toc).getByRole('button', { name: /BİLDİRİ 5/ })).toHaveAttribute('aria-current', 'true');
  });

  it('switches to single pages', async () => {
    const { user } = renderViewer('?sayfa=3');
    await screen.findByDisplayValue('3');

    await user.click(screen.getByRole('button', { name: 'Tek sayfa' }));

    expect(visibleSlots()).toEqual(['3']);
  });

  it('turns pages with the keyboard', async () => {
    const { user } = renderViewer('?sayfa=3');
    await screen.findByDisplayValue('3');

    await user.keyboard('{ArrowRight}');
    expect(visibleSlots()).toEqual(['4', '5']);
    await user.keyboard('{End}');
    expect(visibleSlots()).toEqual(['22']);
    await user.keyboard('{Home}');
    expect(visibleSlots()).toEqual(['1']);
  });

  it('offers download and a new tab', async () => {
    renderViewer();
    await screen.findByDisplayValue('1');

    expect(screen.getByRole('link', { name: 'İndir' })).toHaveAttribute(
      'href',
      '/api/books/kitap-1/pdf?download=true',
    );
    expect(screen.getByRole('link', { name: 'Yeni sekmede aç' })).toHaveAttribute(
      'href',
      '/api/books/kitap-1/pdf',
    );
  });

  it('explains a load error and offers a retry and the download', async () => {
    pdfState.fail = true;
    const { user } = renderViewer();

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('PDF görüntülenemedi');
    expect(within(alert).getByRole('link', { name: /İndir/ })).toHaveAttribute(
      'href',
      '/api/books/kitap-1/pdf?download=true',
    );

    pdfState.fail = false;
    await user.click(within(alert).getByRole('button', { name: 'Tekrar dene' }));
    expect(await screen.findByDisplayValue('1')).toBe(pageBox());
  });
});

describe('PdfViewer on a phone', () => {
  beforeEach(() => {
    setViewportWidth(390);
  });

  it('shows one page with the bottom toolbar and the contents in a sheet', async () => {
    const { user } = renderViewer('?sayfa=3');

    expect(await screen.findByTestId('page-indicator')).toHaveTextContent('3 / 22');
    expect(visibleSlots()).toEqual(['3']);
    expect(screen.queryByRole('navigation', { name: 'Bildiriler' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'İçindekiler' }));
    const sheet = screen.getByRole('dialog', { name: 'İçindekiler' });
    await user.click(within(sheet).getByRole('button', { name: /BİLDİRİ 6/ }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByTestId('page-indicator')).toHaveTextContent('13 / 22');
    expect(visibleSlots()).toEqual(['13']);
  });

  it('zooms with the toolbar buttons and returns to "fit width"', async () => {
    const { user } = renderViewer('?sayfa=3');
    await screen.findByTestId('page-indicator');
    const zoomOut = screen.getByRole('button', { name: 'Uzaklaştır' });
    expect(zoomOut).toBeDisabled();

    await user.click(screen.getByRole('button', { name: 'Yakınlaştır' }));
    expect(zoomOut).toBeEnabled();
    const zoomed = document.querySelector<HTMLElement>('[data-page-slot="3"]')!.style.width;

    // 125 % → 100 % → below the fitted width, which is where zooming out stops.
    await user.click(zoomOut);
    await user.click(zoomOut);
    expect(zoomOut).toBeDisabled();
    expect(document.querySelector<HTMLElement>('[data-page-slot="3"]')!.style.width).not.toBe(zoomed);
    expect(screen.getByTestId('page-indicator')).toHaveTextContent('3 / 22');
  });
});
