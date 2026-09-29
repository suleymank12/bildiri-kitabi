import { ArrowCounterClockwiseIcon, TrashIcon } from '@phosphor-icons/react';
import { errorMessage } from '../../api/errors';
import { useDeletedBooks, useRestoreBook } from '../../api/hooks';
import type { DeletedBookSummary } from '../../api/types';
import { useAnnounce } from '../../app/Announcer';
import { usePageTitle } from '../../app/usePageTitle';
import { Alert, Badge, Button, Card, EmptyState, Skeleton } from '../../components/ui';
import { formatDateTime, formatInteger } from '../../lib/format';
import { statusLabel, statusTone } from '../../lib/status';
import { DELETED_TABLE_COLUMNS, NUMERIC_COLUMN } from '../../lib/tableColumns';
import { Pagination, usePageOutOfRange, usePageParam } from '../library/Pagination';

export const RESTORED_MESSAGE = 'Kitap Kitaplarım’a geri alındı.';

/**
 * Deleted books, most recently deleted first, each with "Geri al". A deleted book cannot be opened (the API answers
 * 404 for it), so its name is plain text here.
 */
export function DeletedBooksPage() {
  usePageTitle('Silinenler');
  const announce = useAnnounce();
  const [page, goTo] = usePageParam();
  const list = useDeletedBooks(page);
  const outOfRange = usePageOutOfRange(page, list);
  const restore = useRestoreBook();
  const data = list.data;

  function restoreBook(book: DeletedBookSummary) {
    restore.mutate(book.uid, {
      onSuccess: () => {
        announce(RESTORED_MESSAGE);
      },
    });
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-3xl sm:text-4xl">Silinenler</h1>
        <p className="text-ink-muted">
          Sildiğiniz kitaplar, en son silinen üstte. Geri aldığınız kitap PDF’iyle birlikte Kitaplarım’a
          döner.
        </p>
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
      {restore.isError && (
        <Alert tone="danger" title="Kitap geri alınamadı">
          {errorMessage(restore.error)}
        </Alert>
      )}

      {list.isPending || outOfRange ? (
        <Card className="flex flex-col gap-3 p-5" aria-busy="true">
          <span className="sr-only">Silinen kitaplar yükleniyor</span>
          {Array.from({ length: 3 }, (_, index) => (
            <Skeleton key={index} className="h-10 w-full" />
          ))}
        </Card>
      ) : data && data.items.length === 0 && page === 1 ? (
        <Card>
          <EmptyState icon={<TrashIcon size={36} />} title="Silinmiş kitap yok.">
            Kitaplarım’da sildiğiniz kitaplar burada görünür ve geri alınabilir.
          </EmptyState>
        </Card>
      ) : data ? (
        <Card className="overflow-visible">
          <div
            aria-hidden="true"
            className={`hidden border-b border-line px-5 py-3 text-xs font-medium tracking-wide text-ink-muted uppercase md:grid ${DELETED_TABLE_COLUMNS}`}
          >
            <span>Kitap</span>
            <span className={NUMERIC_COLUMN}>Bildiri</span>
            <span>Durum</span>
            <span>Silinme</span>
            <span className="sr-only">İşlem</span>
          </div>
          <ul aria-label="Silinen kitaplar" className="flex flex-col">
            {data.items.map((book) => (
              <li
                key={book.uid}
                className={`grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 gap-y-2 border-b border-line px-5 py-4 last:border-b-0 md:items-center ${DELETED_TABLE_COLUMNS}`}
              >
                <span className="flex min-h-11 items-center font-serif text-lg leading-snug font-semibold break-words text-ink">
                  {book.name}
                </span>
                <div className="row-span-2 self-start md:order-last md:row-span-1 md:self-center md:justify-self-end">
                  <Button
                    variant="secondary"
                    size="sm"
                    icon={<ArrowCounterClockwiseIcon size={18} aria-hidden="true" />}
                    loading={restore.isPending && restore.variables === book.uid}
                    aria-label={`${book.name} kitabını geri al`}
                    onClick={() => {
                      restoreBook(book);
                    }}
                  >
                    Geri al
                  </Button>
                </div>
                <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-ink-muted md:contents">
                  <span className={`numeric ${NUMERIC_COLUMN}`}>
                    {formatInteger(book.paperCount)}
                    <span className="md:sr-only"> bildiri</span>
                  </span>
                  <span>
                    <Badge tone={statusTone(book.status)}>{statusLabel(book.status)}</Badge>
                  </span>
                  <span>
                    <span className="md:sr-only">Silinme: </span>
                    {formatDateTime(book.deletedAt)}
                  </span>
                </div>
              </li>
            ))}
          </ul>
        </Card>
      ) : null}

      {data && !outOfRange && <Pagination page={page} totalCount={data.totalCount} onChange={goTo} />}
    </div>
  );
}
