import { Suspense, lazy, useEffect, useRef, useState } from 'react';
import { useParams } from 'react-router';
import { ApiError, errorMessage } from '../../api/errors';
import { useBook, useReorderPapers, useSetPaperTitle, useStartGeneration } from '../../api/hooks';
import type { BookDetail, Paper } from '../../api/types';
import { useAnnounce } from '../../app/Announcer';
import { NotFoundState } from '../../app/NotFoundPage';
import { usePageTitle } from '../../app/usePageTitle';
import { Alert, Button, Card, EmptyState, Skeleton, Stepper } from '../../components/ui';
import { formatDateTime, formatInteger } from '../../lib/format';
import { isBusy, statusLabel } from '../../lib/status';
import { CleanupInfo } from './CleanupInfo';
import { FailurePanel } from './generation/FailurePanel';
import { GenerationProgress } from './generation/GenerationProgress';
import { currentStepLabel } from './generation/stages';
import { PaperOrderList, type PaperMove } from './PaperOrderList';
import type { TitleSaveResult } from './PaperTitleEditor';
import { BOOK_STEPS } from './steps';

// pdf.js is large; it is loaded only when a finished book is opened.
const PdfViewer = lazy(() => import('../viewer/PdfViewer').then((module) => ({ default: module.PdfViewer })));

export function BookPage() {
  const { uid = '' } = useParams();
  const query = useBook(uid);
  const book = query.data;
  usePageTitle(book?.name ?? 'Kitap');
  useStatusAnnouncements(book);

  if (query.isPending) {
    return <BookSkeleton />;
  }

  if (!book) {
    if (query.error instanceof ApiError && query.error.status === 404) {
      return (
        <NotFoundState title="Kitap bulunamadı">
          Bu adreste bir kitap yok; silinmiş veya bağlantı eksik kopyalanmış olabilir.
        </NotFoundState>
      );
    }

    return (
      <EmptyState
        headingLevel="h1"
        title="Kitap yüklenemedi"
        action={
          <Button variant="primary" onClick={() => void query.refetch()}>
            Tekrar dene
          </Button>
        }
      >
        {errorMessage(query.error)}
      </EmptyState>
    );
  }

  return <BookView book={book} />;
}

/** Chooses what the book page shows from the server state; a refresh lands in the same place. */
function BookView({ book }: { book: BookDetail }) {
  const announce = useAnnounce();
  const generate = useStartGeneration(book.uid);
  // "Sırayı düzenle" on a failed book goes back to the order step; the status stays Failed on the server.
  const [editingAfterFailure, setEditingAfterFailure] = useState(false);
  const showOrder = book.status === 'Uploaded' || (book.status === 'Failed' && editingAfterFailure);

  function start() {
    generate.mutate(undefined, {
      onSuccess: () => {
        setEditingAfterFailure(false);
        announce('Kitap hazırlanmak üzere kuyruğa alındı.');
      },
    });
  }

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-5">
        <Stepper steps={BOOK_STEPS} current={showOrder ? 1 : 2} />
        <div className="flex flex-col gap-2">
          <h1 className="text-3xl break-words sm:text-4xl">{book.name}</h1>
          <div className="relative text-sm text-ink-muted">
            <span className="numeric">{book.papers.length}</span> bildiri
            {book.status === 'Completed' && book.pageCount != null && (
              <>
                {' · '}
                <span className="numeric">{formatInteger(book.pageCount)}</span> sayfa
              </>
            )}
            {' · '}
            {formatDateTime(book.createdAt)} · {statusLabel(book.status)}
            {book.status === 'Completed' && <CleanupInfo book={book} />}
          </div>
        </div>
      </div>
      {generate.isError && (
        <Alert tone="danger" title="Kitap oluşturma başlatılamadı">
          {errorMessage(generate.error)}
        </Alert>
      )}
      {showOrder ? (
        <OrderStep book={book} starting={generate.isPending} onStart={start} />
      ) : book.status === 'Failed' ? (
        <FailurePanel
          book={book}
          retrying={generate.isPending}
          onRetry={start}
          onEditOrder={() => {
            setEditingAfterFailure(true);
          }}
        />
      ) : isBusy(book.status) ? (
        <GenerationProgress book={book} />
      ) : (
        // Room at the bottom on phones for the viewer's fixed toolbar.
        <div className="flex flex-col gap-4 pb-20 lg:pb-0">
          <Suspense
            fallback={
              <div aria-busy="true" className="rounded-(--radius) border border-line bg-surface p-6">
                <span className="sr-only">Görüntüleyici yükleniyor</span>
                <Skeleton className="mx-auto aspect-[1/1.414] w-full max-w-md" />
              </div>
            }
          >
            <PdfViewer book={book} />
          </Suspense>
        </div>
      )}
    </div>
  );
}

interface OrderStepProps {
  book: BookDetail;
  starting: boolean;
  onStart: () => void;
}

function OrderStep({ book, starting, onStart }: OrderStepProps) {
  const announce = useAnnounce();
  const reorder = useReorderPapers(book.uid);
  const setTitle = useSetPaperTitle(book.uid);
  const [orderError, setOrderError] = useState<string>();
  const [locked, setLocked] = useState(false);
  const reordered = book.papers.some((paper) => paper.order !== paper.uploadOrder);

  function saveOrder(paperUids: string[], { paper, position }: PaperMove) {
    setOrderError(undefined);
    reorder.mutate(paperUids, {
      onSuccess: () => {
        announce(`${paper.fileName}, ${String(position)}. sıraya taşındı.`);
      },
      onError: (error) => {
        if (error instanceof ApiError && error.code === 'PAPER_ORDER_LOCKED') {
          setLocked(true);
        }

        setOrderError(errorMessage(error));
      },
    });
  }

  async function saveTitle(paper: Paper, title: string): Promise<TitleSaveResult> {
    await setTitle.mutateAsync({ paperUid: paper.uid, title });
    announce(`${paper.fileName} başlığı kaydedildi.`);
    return 'saved';
  }

  return (
    <div className="flex flex-col gap-6">
      {book.status === 'Failed' && (
        <Alert tone="warning" title="Önceki deneme başarısız oldu">
          {book.error?.message ?? 'Beklenmeyen bir hata oluştu.'}
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
            Başlıklar bildirilerden otomatik tespit edildi; yanlış bir başlığı yanındaki kalem düğmesiyle
            düzeltebilirsiniz. Sırayı Yukarı / Aşağı düğmeleriyle değiştirebilirsiniz. Her değişiklik hemen
            kaydedilir.
          </p>
        </div>
        {reordered && (
          <Alert tone="info" title="Sıra, yükleme sırasından farklı.">
            Kitap bu sırayla oluşturulacak.
          </Alert>
        )}
        <PaperOrderList
          papers={book.papers}
          locked={locked || starting}
          onReorder={saveOrder}
          onTitleSave={saveTitle}
        />
      </Card>

      <div className="flex justify-end">
        <Button variant="primary" loading={starting} disabled={locked || reorder.isPending} onClick={onStart}>
          Kitabı Oluştur
        </Button>
      </div>
    </div>
  );
}

/** Reads out stage changes and the result while the page is open; never every percentage. */
function useStatusAnnouncements(book: BookDetail | undefined) {
  const announce = useAnnounce();
  const previous = useRef<{ status: BookDetail['status']; step: string | undefined }>(undefined);
  useEffect(() => {
    if (!book) {
      return;
    }

    const step = currentStepLabel(book);
    const before = previous.current;
    previous.current = { status: book.status, step };
    if (before === undefined) {
      return;
    }

    if (before.status !== book.status && book.status === 'Completed') {
      announce('Kitap hazır. PDF görüntüleyicide açıldı.');
    } else if (before.status !== book.status && book.status === 'Failed') {
      announce('Kitap oluşturulamadı.');
    } else if (step !== undefined && step !== before.step) {
      announce(`${step}.`);
    }
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
