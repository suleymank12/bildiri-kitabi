import {
  ArrowSquareOutIcon,
  ArrowsOutLineHorizontalIcon,
  BookOpenIcon,
  CaretLeftIcon,
  CaretRightIcon,
  CornersInIcon,
  CornersOutIcon,
  DownloadSimpleIcon,
  FileIcon,
  FrameCornersIcon,
  ListIcon,
  MinusIcon,
  PlusIcon,
  SidebarSimpleIcon,
} from '@phosphor-icons/react';
import { useCallback, useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import { Document, Page } from 'react-pdf';
import { useSearchParams } from 'react-router';
import { pdfUrl } from '../../api/hooks';
import type { BookDetail } from '../../api/types';
import { Alert, Button, Dialog, Skeleton, buttonClasses } from '../../components/ui';
import { DESKTOP_QUERY, useMediaQuery } from '../../lib/useMediaQuery';
import { PAGE_PARAM, parsePageParam } from './pageParam';
import { A4, pdfOptions } from './pdfjs';
import { buildSpreads, sideOfPage, spreadIndexOfPage, type ViewMode } from './spreads';
import { ViewerToc, paperOnPages } from './ViewerToc';

type Zoom = { fit: 'page' } | { fit: 'width' } | { scale: number };

const MIN_SCALE = 0.5;
const MAX_SCALE = 3;
const SCALE_STEP = 0.25;
const MAX_PIXEL_RATIO = 2;
const AREA_PADDING = { desktop: 24, phone: 8 };

/** Width and height of an element, kept up to date. */
function useElementSize<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });
  useEffect(() => {
    const element = ref.current;
    if (!element || typeof ResizeObserver === 'undefined') {
      return;
    }

    const observer = new ResizeObserver(([entry]) => {
      if (entry) {
        setSize({ width: entry.contentRect.width, height: entry.contentRect.height });
      }
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, []);
  return [ref, size] as const;
}

function isTyping(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLElement &&
    (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName))
  );
}

/**
 * The finished book in the page: table of contents, page navigation (buttons, page box, keyboard, links inside
 * the PDF), zoom, single or facing pages, full screen and download. The page is kept in `?sayfa=`.
 */
export function PdfViewer({ book }: { book: BookDetail }) {
  const desktop = useMediaQuery(DESKTOP_QUERY);
  const [searchParams, setSearchParams] = useSearchParams();
  const file = pdfUrl(book) ?? '';
  const download = pdfUrl(book, true) ?? '';

  const [numPages, setNumPages] = useState(book.pageCount ?? 0);
  const [pageSize, setPageSize] = useState(A4);
  const [preferredMode, setPreferredMode] = useState<ViewMode>('double');
  const [zoom, setZoom] = useState<Zoom>({ fit: 'page' });
  const [tocOpen, setTocOpen] = useState(true);
  const [mobileTocOpen, setMobileTocOpen] = useState(false);
  const [loadError, setLoadError] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);
  const [fullscreen, setFullscreen] = useState(false);
  const viewerRef = useRef<HTMLDivElement>(null);
  const [areaRef, area] = useElementSize<HTMLDivElement>();

  const mode: ViewMode = desktop ? preferredMode : 'single';
  const padding = desktop ? AREA_PADDING.desktop : AREA_PADDING.phone;
  const effectiveZoom: Zoom = desktop ? zoom : { fit: 'width' };
  const page = parsePageParam(searchParams.get(PAGE_PARAM), numPages);
  const spreads = useMemo(() => buildSpreads(numPages, mode), [numPages, mode]);
  const spreadIndex = spreadIndexOfPage(page, numPages, mode);
  const spread = spreads[spreadIndex] ?? [1];
  const nextSpread = spreads[spreadIndex + 1];
  const activePaper = paperOnPages(book.papers, spread);

  // Page size in CSS pixels: fit the width or the whole page into the area, or an explicit scale.
  const slots = mode === 'double' ? 2 : 1;
  const aspect = pageSize.height / pageSize.width;
  // The observed size is the content box: the area's padding is already taken off.
  const availableWidth = Math.max(160, area.width || 800 - padding * 2);
  const availableHeight = Math.max(200, area.height || 1000 - padding * 2);
  const fitWidth = availableWidth / slots;
  const pageWidth = Math.floor(
    'scale' in effectiveZoom
      ? pageSize.width * effectiveZoom.scale
      : effectiveZoom.fit === 'width'
        ? fitWidth
        : Math.min(fitWidth, availableHeight / aspect),
  );
  const pageHeight = Math.round(pageWidth * aspect);
  // Sharp on high-density screens, but a 3x phone does not need three times the canvas memory.
  const pixelRatio = Math.min(
    MAX_PIXEL_RATIO,
    typeof window === 'undefined' ? 1 : window.devicePixelRatio || 1,
  );
  const scalePercent = Math.round((pageWidth / pageSize.width) * 100);

  const goToPage = useCallback(
    (target: number) => {
      const clamped = Math.min(Math.max(1, Math.round(target)), Math.max(1, numPages));
      setSearchParams(
        (previous) => {
          const next = new URLSearchParams(previous);
          next.set(PAGE_PARAM, String(clamped));
          return next;
        },
        { replace: true },
      );
    },
    [numPages, setSearchParams],
  );

  const goToSpread = useCallback(
    (index: number) => {
      const target = spreads[Math.min(Math.max(0, index), spreads.length - 1)];
      if (target?.[0] !== undefined) {
        goToPage(target[0]);
      }
    },
    [goToPage, spreads],
  );

  const previous = useCallback(() => {
    goToSpread(spreadIndex - 1);
  }, [goToSpread, spreadIndex]);
  const next = useCallback(() => {
    goToSpread(spreadIndex + 1);
  }, [goToSpread, spreadIndex]);

  // Keyboard: arrows, PageUp/PageDown, Home/End — unless the user is typing somewhere.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (
        event.defaultPrevented ||
        event.altKey ||
        event.ctrlKey ||
        event.metaKey ||
        isTyping(event.target)
      ) {
        return;
      }

      const actions: Record<string, (() => void) | undefined> = {
        ArrowLeft: previous,
        PageUp: previous,
        ArrowRight: next,
        PageDown: next,
        Home: () => {
          goToPage(1);
        },
        End: () => {
          goToPage(numPages);
        },
      };
      const action = actions[event.key];
      if (action) {
        event.preventDefault();
        action();
      }
    }

    window.addEventListener('keydown', onKeyDown);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
    };
  }, [goToPage, next, numPages, previous]);

  useEffect(() => {
    function onChange() {
      setFullscreen(document.fullscreenElement === viewerRef.current);
    }

    document.addEventListener('fullscreenchange', onChange);
    return () => {
      document.removeEventListener('fullscreenchange', onChange);
    };
  }, []);

  function toggleFullscreen() {
    if (document.fullscreenElement) {
      void document.exitFullscreen();
    } else {
      void viewerRef.current?.requestFullscreen();
    }
  }

  function changeScale(direction: 1 | -1) {
    const current = pageWidth / pageSize.width;
    const stepped = Math.round((current + direction * SCALE_STEP) / SCALE_STEP) * SCALE_STEP;
    setZoom({ scale: Math.min(MAX_SCALE, Math.max(MIN_SCALE, stepped)) });
  }

  if (loadError) {
    return (
      <Alert
        tone="danger"
        title="PDF görüntülenemedi"
        action={
          <div className="flex flex-col gap-2 sm:flex-row">
            <Button
              variant="secondary"
              onClick={() => {
                setLoadError(false);
                setReloadKey((key) => key + 1);
              }}
            >
              Tekrar dene
            </Button>
            <a href={download} className={buttonClasses('secondary')}>
              <DownloadSimpleIcon size={18} aria-hidden="true" />
              İndir
            </a>
          </div>
        }
      >
        Kitabın PDF’i yüklenemedi. Bağlantınızı kontrol edip tekrar deneyin veya dosyayı indirin.
      </Alert>
    );
  }

  const pageIndicator = `${String(page)} / ${String(numPages)}`;
  const slot = (pageNumber: number | undefined, key: string): ReactNode => (
    <div
      key={key}
      className={
        pageNumber === undefined ? 'shrink-0' : 'relative shrink-0 bg-surface shadow-(--shadow-raised)'
      }
      style={{ width: pageWidth, height: pageHeight }}
      data-page-slot={pageNumber}
    >
      {pageNumber !== undefined && (
        <Page
          pageNumber={pageNumber}
          width={pageWidth}
          devicePixelRatio={pixelRatio}
          renderTextLayer
          renderAnnotationLayer
          loading={<Skeleton className="absolute inset-0" />}
        />
      )}
    </div>
  );

  // Facing pages: even on the left, odd on the right; an empty slot keeps the cover and the last page in place.
  const visibleSlots =
    mode === 'double'
      ? [spread.find((p) => sideOfPage(p) === 'left'), spread.find((p) => sideOfPage(p) === 'right')]
      : [spread[0]];

  const toc = (
    <ViewerToc
      papers={book.papers}
      activePaperId={activePaper?.id}
      onSelect={(target) => {
        setMobileTocOpen(false);
        goToPage(target);
      }}
    />
  );

  return (
    <section
      ref={viewerRef}
      aria-label="PDF görüntüleyici"
      // Lets the page layout widen to give the facing pages room (see Layout).
      data-wide-page=""
      className={
        'flex flex-col overflow-hidden rounded-(--radius) border border-line bg-surface ' +
        (fullscreen ? 'h-dvh rounded-none' : 'lg:h-[calc(100dvh-5rem)] lg:min-h-[560px]')
      }
    >
      {desktop && (
        <div
          role="toolbar"
          aria-label="Görüntüleyici araçları"
          className="flex flex-wrap items-center gap-x-4 gap-y-2 border-b border-line px-3 py-2"
        >
          <ToolButton
            label={tocOpen ? 'İçindekileri gizle' : 'İçindekileri göster'}
            pressed={tocOpen}
            onClick={() => {
              setTocOpen((open) => !open);
            }}
          >
            <SidebarSimpleIcon size={20} />
          </ToolButton>

          <div className="flex items-center gap-1">
            <ToolButton label="Önceki sayfa" disabled={spreadIndex === 0} onClick={previous}>
              <CaretLeftIcon size={20} />
            </ToolButton>
            <PageInput page={page} numPages={numPages} onGo={goToPage} />
            <ToolButton label="Sonraki sayfa" disabled={spreadIndex >= spreads.length - 1} onClick={next}>
              <CaretRightIcon size={20} />
            </ToolButton>
          </div>

          <div className="flex items-center gap-1">
            <ToolButton
              label="Uzaklaştır"
              disabled={pageWidth / pageSize.width <= MIN_SCALE}
              onClick={() => {
                changeScale(-1);
              }}
            >
              <MinusIcon size={20} />
            </ToolButton>
            <span className="numeric w-12 text-center text-sm text-ink-muted" aria-live="polite">
              %{scalePercent}
            </span>
            <ToolButton
              label="Yakınlaştır"
              disabled={pageWidth / pageSize.width >= MAX_SCALE}
              onClick={() => {
                changeScale(1);
              }}
            >
              <PlusIcon size={20} />
            </ToolButton>
            <ToolButton
              label="Sayfaya sığdır"
              pressed={'fit' in zoom && zoom.fit === 'page'}
              onClick={() => {
                setZoom({ fit: 'page' });
              }}
            >
              <FrameCornersIcon size={20} />
            </ToolButton>
            <ToolButton
              label="Genişliğe sığdır"
              pressed={'fit' in zoom && zoom.fit === 'width'}
              onClick={() => {
                setZoom({ fit: 'width' });
              }}
            >
              <ArrowsOutLineHorizontalIcon size={20} />
            </ToolButton>
          </div>

          <div role="group" aria-label="Görünüm" className="flex items-center gap-1">
            <ToolButton
              label="Tek sayfa"
              pressed={mode === 'single'}
              showLabel
              onClick={() => {
                setPreferredMode('single');
              }}
            >
              <FileIcon size={18} />
            </ToolButton>
            <ToolButton
              label="Çift sayfa"
              pressed={mode === 'double'}
              showLabel
              onClick={() => {
                setPreferredMode('double');
              }}
            >
              <BookOpenIcon size={18} />
            </ToolButton>
          </div>

          <div className="ml-auto flex items-center gap-1">
            <ToolButton label={fullscreen ? 'Tam ekrandan çık' : 'Tam ekran'} onClick={toggleFullscreen}>
              {fullscreen ? <CornersInIcon size={20} /> : <CornersOutIcon size={20} />}
            </ToolButton>
            <a
              href={file}
              target="_blank"
              rel="noopener"
              aria-label="Yeni sekmede aç"
              title="Yeni sekmede aç"
              className={buttonClasses('ghost', 'sm', 'w-11 px-0')}
            >
              <ArrowSquareOutIcon size={20} aria-hidden="true" />
            </a>
            <a href={download} className={buttonClasses('primary', 'sm')}>
              <DownloadSimpleIcon size={18} aria-hidden="true" />
              İndir
            </a>
          </div>
        </div>
      )}

      <div className="flex min-h-0 flex-1">
        {desktop && tocOpen && (
          <nav
            aria-label="Bildiriler"
            className="relative w-64 shrink-0 overflow-y-auto border-r border-line bg-surface px-2 py-3"
          >
            <h2 className="px-3 pb-2 text-lg">İçindekiler</h2>
            {toc}
          </nav>
        )}

        <div
          ref={areaRef}
          className="relative min-w-0 flex-1 overflow-auto bg-surface-muted"
          style={{ padding }}
        >
          <Document
            key={reloadKey}
            file={file}
            options={pdfOptions}
            onLoadSuccess={(document) => {
              setNumPages(document.numPages);
              void document.getPage(1).then((first) => {
                const viewport = first.getViewport({ scale: 1 });
                setPageSize({ width: viewport.width, height: viewport.height });
              });
            }}
            onLoadError={() => {
              setLoadError(true);
            }}
            onItemClick={({ pageNumber }) => {
              goToPage(pageNumber);
            }}
            externalLinkTarget="_blank"
            loading={
              <div className="flex justify-center">
                <div style={{ width: pageWidth * slots, height: pageHeight }}>
                  <Skeleton className="size-full" />
                </div>
              </div>
            }
          >
            <div className="flex min-w-fit justify-center">
              {visibleSlots.map((pageNumber, index) => slot(pageNumber, `slot-${String(index)}`))}
            </div>
            {nextSpread && (
              // The next spread is drawn out of sight so turning the page shows it at once.
              <div aria-hidden="true" className="hidden">
                {nextSpread.map((pageNumber) => (
                  <Page
                    key={pageNumber}
                    pageNumber={pageNumber}
                    width={pageWidth}
                    devicePixelRatio={pixelRatio}
                    renderTextLayer={false}
                    renderAnnotationLayer={false}
                  />
                ))}
              </div>
            )}
          </Document>
          <p className="sr-only" aria-live="polite">
            Sayfa {pageIndicator}
          </p>
        </div>
      </div>

      {!desktop && (
        <div
          role="toolbar"
          aria-label="Görüntüleyici araçları"
          className="fixed inset-x-0 bottom-0 z-30 flex items-center justify-between gap-2 border-t border-line bg-surface px-3 py-2 pb-[max(0.5rem,env(safe-area-inset-bottom))]"
        >
          <ToolButton label="Önceki sayfa" disabled={spreadIndex === 0} onClick={previous}>
            <CaretLeftIcon size={22} />
          </ToolButton>
          <span className="numeric text-sm text-ink" data-testid="page-indicator">
            {pageIndicator}
          </span>
          <ToolButton label="Sonraki sayfa" disabled={spreadIndex >= spreads.length - 1} onClick={next}>
            <CaretRightIcon size={22} />
          </ToolButton>
          <ToolButton
            label="İçindekiler"
            showLabel
            onClick={() => {
              setMobileTocOpen(true);
            }}
          >
            <ListIcon size={20} />
          </ToolButton>
          <a href={download} aria-label="İndir" className={buttonClasses('primary', 'sm', 'w-11 px-0')}>
            <DownloadSimpleIcon size={20} aria-hidden="true" />
          </a>
        </div>
      )}

      <Dialog
        open={!desktop && mobileTocOpen}
        placement="bottom"
        title="İçindekiler"
        onClose={() => {
          setMobileTocOpen(false);
        }}
        actions={
          <Button
            variant="secondary"
            onClick={() => {
              setMobileTocOpen(false);
            }}
          >
            Kapat
          </Button>
        }
      >
        {toc}
      </Dialog>
    </section>
  );
}

interface ToolButtonProps {
  label: string;
  onClick: () => void;
  children: ReactNode;
  disabled?: boolean;
  /** For toggles; sets `aria-pressed`. */
  pressed?: boolean;
  /** Shows the label next to the icon instead of only as a tooltip. */
  showLabel?: boolean;
}

function ToolButton({
  label,
  onClick,
  children,
  disabled = false,
  pressed,
  showLabel = false,
}: ToolButtonProps) {
  return (
    <button
      type="button"
      title={showLabel ? undefined : label}
      aria-label={showLabel ? undefined : label}
      aria-pressed={pressed}
      disabled={disabled}
      onClick={onClick}
      className={
        'inline-flex min-h-11 min-w-11 items-center justify-center gap-2 rounded-(--radius) px-2 text-sm font-medium transition-colors duration-[130ms] motion-reduce:transition-none disabled:opacity-40 ' +
        (pressed
          ? 'bg-accent-soft text-accent'
          : 'text-ink-muted hover:enabled:bg-paper hover:enabled:text-ink active:enabled:bg-line')
      }
    >
      <span aria-hidden="true">{children}</span>
      {showLabel && <span>{label}</span>}
    </button>
  );
}

function PageInput({
  page,
  numPages,
  onGo,
}: {
  page: number;
  numPages: number;
  onGo: (page: number) => void;
}) {
  const [draft, setDraft] = useState<string>();
  const totalId = useId();
  const value = draft ?? String(page);

  function commit() {
    const target = Number.parseInt(value, 10);
    if (Number.isFinite(target)) {
      onGo(target);
    }

    setDraft(undefined);
  }

  return (
    <div className="flex items-center gap-2 text-sm text-ink-muted">
      <input
        aria-label="Sayfa numarası"
        aria-describedby={totalId}
        inputMode="numeric"
        value={value}
        onChange={(event) => {
          setDraft(event.target.value.replace(/\D/g, ''));
        }}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            commit();
          } else if (event.key === 'Escape') {
            setDraft(undefined);
          }
        }}
        className="numeric h-11 w-14 rounded-(--radius) border border-ink-subtle bg-surface text-center text-ink"
      />
      <span aria-hidden="true">
        / <span className="numeric">{numPages}</span>
      </span>
      <span id={totalId} className="sr-only">
        toplam {numPages} sayfa
      </span>
    </div>
  );
}
