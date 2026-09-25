import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../api/errors';
import { useBook, useReorderPapers, useStartGeneration } from '../../api/hooks';
import type { BookDetail } from '../../api/types';
import { useAnnounce } from '../../app/Announcer';
import { paths } from '../../app/paths';
import { usePageTitle } from '../../app/usePageTitle';
import { Alert, Button, Card, EmptyState, Skeleton, Stepper, buttonClasses } from '../../components/ui';
import { formatDateTime } from '../../lib/format';
import { isEditable, statusLabel } from '../../lib/status';
import { GenerationPanel } from './GenerationPanel';
import { PaperOrderList } from './PaperOrderList';
import { BOOK_STEPS } from './steps';

export function BookPage() {
  const { id = '' } = useParams();
  const query = useBook(id);
  const book = query.data;
  usePageTitle(book?.name ?? 'Kitap');
  useStatusAnnouncements(book);

  if (query.isPending) {
    return <BookSkeleton />;
  }

  if (!book) {
    const notFound = query.error instanceof ApiError && query.error.status === 404;
    return (
      <EmptyState
        title={notFound ? 'Kitap bulunamadı' : 'Kitap yüklenemedi'}
        action={
          notFound ? (
            <Link to={paths.library} className={buttonClasses('primary')}>
              Kitaplarım
            </Link>
          ) : (
            <Button variant="primary" onClick={() => void query.refetch()}>
              Tekrar dene
            </Button>
          )
        }
      >
        {notFound ? 'Kitap silinmiş olabilir.' : errorMessage(query.error)}
      </EmptyState>
    );
  }

  const editable = isEditable(book.status);
  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-5">
        <Stepper steps={BOOK_STEPS} current={editable ? 1 : 2} />
        <div className="flex flex-col gap-2">
          <h1 className="text-3xl break-words sm:text-4xl">{book.name}</h1>
          <p className="text-sm text-ink-muted">
            <span className="numeric">{book.papers.length}</span> bildiri · {formatDateTime(book.createdAt)} ·{' '}
            {statusLabel(book.status)}
          </p>
        </div>
      </div>
      {editable ? <OrderStep book={book} /> : <GenerationPanel book={book} />}
    </div>
  );
}

function OrderStep({ book }: { book: BookDetail }) {
  const announce = useAnnounce();
  const reorder = useReorderPapers(book.id);
  const generate = useStartGeneration(book.id);
  const [orderError, setOrderError] = useState<string>();
  const [locked, setLocked] = useState(false);
  const reordered = book.papers.some((paper) => paper.order !== paper.uploadOrder);

  function saveOrder(paperIds: string[]) {
    setOrderError(undefined);
    reorder.mutate(paperIds, {
      onSuccess: () => {
        announce('Sıra kaydedildi.');
      },
      onError: (error) => {
        if (error instanceof ApiError && error.code === 'PAPER_ORDER_LOCKED') {
          setLocked(true);
        }

        setOrderError(errorMessage(error));
      },
    });
  }

  function start() {
    generate.mutate(undefined, {
      onSuccess: () => {
        announce('Kitap hazırlanmak üzere kuyruğa alındı.');
      },
    });
  }

  return (
    <div className="flex flex-col gap-6">
      {book.status === 'Failed' && (
        <Alert
          tone="danger"
          title="Kitap oluşturulamadı"
          action={
            <Button variant="secondary" loading={generate.isPending} onClick={start}>
              Tekrar dene
            </Button>
          }
        >
          {book.error?.message ?? 'Beklenmeyen bir hata oluştu.'}
        </Alert>
      )}
      {generate.isError && (
        <Alert tone="danger" title="Kitap oluşturma başlatılamadı">
          {errorMessage(generate.error)}
        </Alert>
      )}
      {orderError && (
        <Alert tone="danger" title="Sıra kaydedilemedi">
          {orderError}
        </Alert>
      )}

      <Card className="flex flex-col gap-5 p-5 sm:p-6">
        <div className="flex flex-col gap-1">
          <h2 className="text-2xl">Sıra ve kontrol</h2>
          <p className="text-sm text-ink-muted">
            Başlıklar bildirilerden otomatik tespit edildi. Sırayı sürükleyerek veya Yukarı / Aşağı
            düğmeleriyle değiştirebilirsiniz; her değişiklik hemen kaydedilir.
          </p>
        </div>
        {reordered && (
          <Alert tone="info" title="Sıra, yükleme sırasından farklı.">
            Kitap bu sırayla oluşturulacak.
          </Alert>
        )}
        <PaperOrderList papers={book.papers} locked={locked || generate.isPending} onReorder={saveOrder} />
      </Card>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-ink-muted">
          Kitap oluşturulunca kapak, İçindekiler ve sayfa numaraları eklenir; iletişim bilgileri temizlenir.
        </p>
        <Button
          variant="primary"
          loading={generate.isPending}
          disabled={locked || reorder.isPending}
          onClick={start}
        >
          Kitabı Oluştur
        </Button>
      </div>
    </div>
  );
}

/** Reads out status changes that happen while the page is open (queued → processing → ready). */
function useStatusAnnouncements(book: BookDetail | undefined) {
  const announce = useAnnounce();
  const previous = useRef<BookDetail['status']>(undefined);
  useEffect(() => {
    if (!book) {
      return;
    }

    if (previous.current !== undefined && previous.current !== book.status) {
      announce(
        book.status === 'Completed'
          ? 'Kitap hazır. PDF’i açabilir veya indirebilirsiniz.'
          : book.status === 'Failed'
            ? 'Kitap oluşturulamadı.'
            : `Durum: ${statusLabel(book.status)}.`,
      );
    }

    previous.current = book.status;
  }, [announce, book]);
}

function BookSkeleton() {
  return (
    <div className="flex flex-col gap-6" aria-busy="true">
      <span className="sr-only">Kitap yükleniyor</span>
      <Skeleton className="h-7 w-72" />
      <Skeleton className="h-10 w-2/3" />
      <Card className="flex flex-col gap-3 p-6">
        {Array.from({ length: 5 }, (_, index) => (
          <Skeleton key={index} className="h-12 w-full" />
        ))}
      </Card>
    </div>
  );
}
