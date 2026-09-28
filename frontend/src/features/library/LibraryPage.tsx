import { BookOpenIcon, CaretLeftIcon, CaretRightIcon, PencilSimpleIcon, PlusIcon, TrashIcon } from '@phosphor-icons/react';
import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { errorMessage } from '../../api/errors';
import { LIST_PAGE_SIZE, useBookList } from '../../api/hooks';
import type { BookSummary } from '../../api/types';
import { paths } from '../../app/paths';
import { usePageTitle } from '../../app/usePageTitle';
import { Alert, Badge, Button, Card, EmptyState, Skeleton, Tooltip } from '../../components/ui';
import { formatDateTime, formatInteger } from '../../lib/format';
import { isBusy, statusLabel, statusTone } from '../../lib/status';
import { LIBRARY_TABLE_COLUMNS, NUMERIC_COLUMN } from '../../lib/tableColumns';
import { NewBookDialog } from '../new-book/NewBookDialog';
import { DeleteBookDialog } from './DeleteBookDialog';

export const EDIT_BLOCKED_REASON = 'Kitap oluşturulurken düzenlenemez.';
export const DELETE_BLOCKED_REASON = 'Kitap oluşturulurken silinemez.';

export function LibraryPage() {
  usePageTitle('Kitaplarım');
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number.parseInt(searchParams.get('sayfa') ?? '1', 10) || 1);
  const list = useBookList(page);
  const [pendingDelete, setPendingDelete] = useState<BookSummary>();
  const [creating, setCreating] = useState(false);

  const data = list.data;
  const pageCount = data ? Math.max(1, Math.ceil(data.totalCount / LIST_PAGE_SIZE)) : 1;

  function goTo(target: number) {
    setSearchParams(target === 1 ? {} : { sayfa: String(target) });
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex flex-col gap-1">
          <h1 className="text-3xl sm:text-4xl">Kitaplarım</h1>
          <p className="text-ink-muted">Oluşturduğunuz kitaplar, en yenisi üstte.</p>
        </div>
        <Button
          variant="primary"
          icon={<PlusIcon size={18} aria-hidden="true" />}
          onClick={() => {
            setCreating(true);
          }}
        >
          Yeni kitap
        </Button>
      </div>

      {list.isError && (
        <Alert
          tone="danger"
          title="Liste yüklenemedi"
          action={
            <Button variant="secondary" onClick={() => void list.refetch()}>
              Tekrar dene
            </Button>
          }
        >
          {errorMessage(list.error)}
        </Alert>
      )}

      {list.isPending ? (
        <ListSkeleton />
      ) : data && data.items.length === 0 && page === 1 ? (
        <Card>
          <EmptyState
            icon={<BookOpenIcon size={36} />}
            title="Henüz kitap oluşturmadınız."
            action={
              <Button
                variant="primary"
                icon={<PlusIcon size={18} aria-hidden="true" />}
                onClick={() => {
                  setCreating(true);
                }}
              >
                Yeni kitap
              </Button>
            }
          >
            On bildiri dosyasını yükleyerek ilk kitabınızı oluşturun.
          </EmptyState>
        </Card>
      ) : data ? (
        <Card className="overflow-visible">
          <div
            aria-hidden="true"
            className={`hidden border-b border-line px-5 py-3 text-xs font-medium tracking-wide text-ink-muted uppercase md:grid ${LIBRARY_TABLE_COLUMNS}`}
          >
            <span>Kitap</span>
            <span>Durum</span>
            <span className={NUMERIC_COLUMN}>Bildiri</span>
            <span className={NUMERIC_COLUMN}>Sayfa</span>
            <span>Oluşturulma</span>
            <span className="sr-only">İşlem</span>
          </div>
          <ul aria-label="Kitaplar" className="flex flex-col">
            {data.items.map((book) => (
              <BookRow
                key={book.uid}
                book={book}
                onDelete={() => {
                  setPendingDelete(book);
                }}
              />
            ))}
          </ul>
        </Card>
      ) : null}

      {data && pageCount > 1 && (
        <nav aria-label="Sayfalar" className="flex items-center justify-between gap-3">
          <Button
            variant="secondary"
            size="sm"
            icon={<CaretLeftIcon size={16} aria-hidden="true" />}
            disabled={page <= 1}
            onClick={() => {
              goTo(page - 1);
            }}
          >
            Önceki
          </Button>
          <span className="text-sm text-ink-muted">
            Sayfa <span className="numeric">{page}</span> / <span className="numeric">{pageCount}</span>
          </span>
          <Button
            variant="secondary"
            size="sm"
            disabled={page >= pageCount}
            onClick={() => {
              goTo(page + 1);
            }}
          >
            Sonraki
            <CaretRightIcon size={16} aria-hidden="true" />
          </Button>
        </nav>
      )}

      <NewBookDialog
        open={creating}
        onClose={() => {
          setCreating(false);
        }}
      />

      <DeleteBookDialog
        book={pendingDelete}
        onClose={() => {
          setPendingDelete(undefined);
        }}
      />
    </div>
  );
}

function BookRow({ book, onDelete }: { book: BookSummary; onDelete: () => void }) {
  const busy = isBusy(book.status);
  const navigate = useNavigate();
  return (
    <li
      className={`relative grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 gap-y-2 border-b border-line px-5 py-4 last:border-b-0 hover:bg-surface-muted md:items-center ${LIBRARY_TABLE_COLUMNS}`}
    >
      <Link
        to={paths.book(book.uid)}
        className="flex min-h-11 items-center font-serif text-lg leading-snug font-semibold break-words text-ink after:absolute after:inset-0 after:content-[''] hover:underline"
      >
        {book.name}
      </Link>
      {/* Both stay focusable while the book is being generated, so the reason in the tooltip can be read. */}
      <div className="relative z-10 row-span-2 flex items-center gap-1 self-start md:order-last md:row-span-1 md:justify-end md:self-center">
        <Tooltip align="end" content={busy ? EDIT_BLOCKED_REASON : undefined}>
          {(tooltip) => (
            <Button
              variant="ghost"
              size="sm"
              className="w-11 px-0 md:w-auto md:px-3"
              icon={<PencilSimpleIcon size={18} aria-hidden="true" />}
              softDisabled={busy}
              aria-label={`${book.name} kitabını düzenle`}
              {...tooltip}
              onClick={() => {
                void navigate(paths.editBook(book.uid));
              }}
            >
              <span className="hidden md:inline">Düzenle</span>
            </Button>
          )}
        </Tooltip>
        <Tooltip align="end" content={busy ? DELETE_BLOCKED_REASON : undefined}>
          {(tooltip) => (
            <Button
              variant="danger-quiet"
              size="sm"
              className="w-11 px-0 md:w-auto md:px-3"
              icon={<TrashIcon size={18} aria-hidden="true" />}
              softDisabled={busy}
              aria-label={`${book.name} kitabını sil`}
              {...tooltip}
              onClick={onDelete}
            >
              <span className="hidden md:inline">Sil</span>
            </Button>
          )}
        </Tooltip>
      </div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-ink-muted md:contents">
        <span>
          <Badge tone={statusTone(book.status)}>{statusLabel(book.status)}</Badge>
        </span>
        <span className={`numeric ${NUMERIC_COLUMN}`}>
          {formatInteger(book.paperCount)}
          <span className="md:sr-only"> bildiri</span>
        </span>
        <span className={`numeric ${NUMERIC_COLUMN}`}>
          {book.pageCount != null ? formatInteger(book.pageCount) : '–'}
          <span className="md:sr-only"> sayfa</span>
        </span>
        <span>{formatDateTime(book.createdAt)}</span>
      </div>
    </li>
  );
}

function ListSkeleton() {
  return (
    <Card className="flex flex-col gap-3 p-5" aria-busy="true">
      <span className="sr-only">Kitaplar yükleniyor</span>
      {Array.from({ length: 4 }, (_, index) => (
        <Skeleton key={index} className="h-10 w-full" />
      ))}
    </Card>
  );
}
