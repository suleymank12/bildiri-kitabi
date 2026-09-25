import { BookOpenIcon, CaretLeftIcon, CaretRightIcon, TrashIcon } from '@phosphor-icons/react';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { errorMessage } from '../../api/errors';
import { LIST_PAGE_SIZE, useBookList, useDeleteBook } from '../../api/hooks';
import type { BookSummary } from '../../api/types';
import { useAnnounce } from '../../app/Announcer';
import { paths } from '../../app/paths';
import { usePageTitle } from '../../app/usePageTitle';
import { Alert, Badge, Button, Card, Dialog, EmptyState, Skeleton, buttonClasses } from '../../components/ui';
import { formatDateTime, formatInteger } from '../../lib/format';
import { isBusy, statusLabel, statusTone } from '../../lib/status';
import { RowMenu } from './RowMenu';

export function LibraryPage() {
  usePageTitle('Kitaplarım');
  const announce = useAnnounce();
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number.parseInt(searchParams.get('sayfa') ?? '1', 10) || 1);
  const list = useBookList(page);
  const deleteBook = useDeleteBook();
  const [pendingDelete, setPendingDelete] = useState<BookSummary>();

  const data = list.data;
  const pageCount = data ? Math.max(1, Math.ceil(data.totalCount / LIST_PAGE_SIZE)) : 1;

  function goTo(target: number) {
    setSearchParams(target === 1 ? {} : { sayfa: String(target) });
  }

  function confirmDelete() {
    if (!pendingDelete) {
      return;
    }

    const book = pendingDelete;
    deleteBook.mutate(book.id, {
      onSuccess: () => {
        setPendingDelete(undefined);
        announce(`“${book.name}” silindi.`);
      },
    });
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
        <div className="flex flex-col gap-1">
          <h1 className="text-3xl sm:text-4xl">Kitaplarım</h1>
          <p className="text-ink-muted">Oluşturduğunuz kitaplar, en yenisi üstte.</p>
        </div>
        <Link to={paths.newBook} className={buttonClasses('primary')}>
          Yeni kitap
        </Link>
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
      {deleteBook.isError && !pendingDelete && (
        <Alert tone="danger" title="Kitap silinemedi">
          {errorMessage(deleteBook.error)}
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
              <Link to={paths.newBook} className={buttonClasses('primary')}>
                Yeni kitap
              </Link>
            }
          >
            On bildiri dosyasını yükleyerek ilk kitabınızı oluşturun.
          </EmptyState>
        </Card>
      ) : data ? (
        <Card className="overflow-visible">
          <div
            aria-hidden="true"
            className="hidden grid-cols-[minmax(0,1fr)_8rem_5rem_5rem_11rem_3rem] gap-4 border-b border-line px-5 py-3 text-xs font-medium tracking-wide text-ink-muted uppercase md:grid"
          >
            <span>Kitap</span>
            <span>Durum</span>
            <span className="text-right">Bildiri</span>
            <span className="text-right">Sayfa</span>
            <span>Oluşturulma</span>
            <span />
          </div>
          <ul aria-label="Kitaplar" className="flex flex-col">
            {data.items.map((book) => (
              <BookRow
                key={book.id}
                book={book}
                onDelete={() => {
                  deleteBook.reset();
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

      <Dialog
        open={pendingDelete !== undefined}
        title="Kitabı sil"
        onClose={() => {
          setPendingDelete(undefined);
        }}
        actions={
          <>
            <Button
              variant="secondary"
              disabled={deleteBook.isPending}
              onClick={() => {
                setPendingDelete(undefined);
              }}
            >
              Vazgeç
            </Button>
            <Button variant="danger" loading={deleteBook.isPending} onClick={confirmDelete}>
              Sil
            </Button>
          </>
        }
      >
        <p>
          “{pendingDelete?.name}” kitabı, bildirileri ve PDF’i kalıcı olarak silinecek. Bu işlem geri
          alınamaz.
        </p>
        {deleteBook.isError && <p className="mt-3 text-sm text-danger">{errorMessage(deleteBook.error)}</p>}
      </Dialog>
    </div>
  );
}

function BookRow({ book, onDelete }: { book: BookSummary; onDelete: () => void }) {
  const busy = isBusy(book.status);
  return (
    <li className="relative grid grid-cols-[minmax(0,1fr)_3rem] gap-x-3 gap-y-2 border-b border-line px-5 py-4 last:border-b-0 hover:bg-surface-muted md:grid-cols-[minmax(0,1fr)_8rem_5rem_5rem_11rem_3rem] md:items-center md:gap-4">
      <Link
        to={paths.book(book.id)}
        className="font-serif text-lg leading-snug font-semibold break-words text-ink after:absolute after:inset-0 after:content-[''] hover:underline"
      >
        {book.name}
      </Link>
      <div className="relative z-10 row-span-2 self-start md:order-last md:row-span-1 md:self-center">
        <RowMenu
          label={`${book.name} için işlemler`}
          items={[
            {
              label: 'Sil',
              icon: <TrashIcon size={16} aria-hidden="true" />,
              tone: 'danger',
              disabled: busy,
              disabledReason: 'Kitap hazırlanırken silinemez.',
              onSelect: onDelete,
            },
          ]}
        />
      </div>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-ink-muted md:contents">
        <span>
          <Badge tone={statusTone(book.status)}>{statusLabel(book.status)}</Badge>
        </span>
        <span className="numeric md:text-right">
          {formatInteger(book.paperCount)}
          <span className="font-sans md:sr-only"> bildiri</span>
        </span>
        <span className="numeric md:text-right">
          {book.pageCount != null ? formatInteger(book.pageCount) : '–'}
          <span className="font-sans md:sr-only"> sayfa</span>
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
