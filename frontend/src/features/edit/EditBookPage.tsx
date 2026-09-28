import { ArrowLeftIcon, BookOpenIcon, FloppyDiskIcon, TrashIcon } from '@phosphor-icons/react';
import { useRef, useState, type SubmitEvent } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { ApiError, errorMessage } from '../../api/errors';
import {
  useBook,
  useRenameBook,
  useReorderPapers,
  useSetPaperTitle,
  useStartGeneration,
} from '../../api/hooks';
import type { BookDetail, Paper } from '../../api/types';
import { useAnnounce } from '../../app/Announcer';
import { NotFoundState } from '../../app/NotFoundPage';
import { paths } from '../../app/paths';
import { usePageTitle } from '../../app/usePageTitle';
import {
  Alert,
  Button,
  Card,
  Dialog,
  EmptyState,
  Skeleton,
  TextField,
  buttonClasses,
} from '../../components/ui';
import { bookNameError } from '../../lib/files';
import { BOOK_NAME_MAX } from '../../lib/limits';
import { isBusy } from '../../lib/status';
import { MissingTitlesAlert } from '../book/MissingTitlesAlert';
import { PaperOrderList, type PaperMove } from '../book/PaperOrderList';
import type { TitleSaveResult } from '../book/PaperTitleEditor';
import { DeleteBookDialog } from '../library/DeleteBookDialog';

export const COMPLETED_NOTICE =
  'Bu kitap oluşturuldu. Adı, sırayı veya bir başlığı değiştirirseniz mevcut PDF silinir ve kitabı yeniden oluşturmanız gerekir.';
export const CONFLICT_MESSAGE =
  'Kitap bu sırada başka bir işlemle değişti. Güncel hali yüklendi, lütfen tekrar deneyin.';
const BUSY_MESSAGE = 'Kitap oluşturulurken düzenlenemez.';

export function EditBookPage() {
  const { uid = '' } = useParams();
  const query = useBook(uid);
  const book = query.data;
  usePageTitle(book ? `${book.name} — Düzenle` : 'Kitabı düzenle');

  if (query.isPending) {
    return (
      <div className="flex flex-col gap-6" aria-busy="true">
        <span className="sr-only">Kitap yükleniyor</span>
        <Skeleton className="h-10 w-2/3" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }

  if (!book) {
    if (query.error instanceof ApiError && query.error.status === 404) {
      return (
        <NotFoundState title="Kitap bulunamadı">
          Bu adreste bir kitap yok; silinmiş olabilir. Silinen kitaplar Silinenler’den geri alınabilir.
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

  return <EditView book={book} />;
}

/** A 409 means the book changed under us: the hooks reload it and the page says so. */
function isConflict(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409;
}

function EditView({ book }: { book: BookDetail }) {
  const navigate = useNavigate();
  const announce = useAnnounce();
  const rename = useRenameBook(book.uid);
  const reorder = useReorderPapers(book.uid);
  const setTitle = useSetPaperTitle(book.uid);
  const generate = useStartGeneration(book.uid);
  const [name, setName] = useState(book.name);
  const [nameError, setNameError] = useState<string>();
  const [conflict, setConflict] = useState(false);
  const [orderError, setOrderError] = useState<string>();
  const [deleting, setDeleting] = useState(false);
  // The first change to a completed book asks first; the answer is awaited by the change that asked.
  const [confirming, setConfirming] = useState(false);
  const confirmation = useRef<(proceed: boolean) => void>(undefined);
  const busy = isBusy(book.status);

  function confirmReopen(): Promise<boolean> {
    if (book.status !== 'Completed') {
      return Promise.resolve(true);
    }

    setConfirming(true);
    return new Promise((resolve) => {
      confirmation.current = resolve;
    });
  }

  function answer(proceed: boolean) {
    setConfirming(false);
    confirmation.current?.(proceed);
    confirmation.current = undefined;
  }

  function handleError(error: unknown): string {
    if (isConflict(error)) {
      setConflict(true);
      announce(CONFLICT_MESSAGE);
      return CONFLICT_MESSAGE;
    }

    return errorMessage(error);
  }

  async function saveName(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    const problem = bookNameError(name);
    setNameError(problem);
    if (problem || name.trim() === book.name) {
      return;
    }

    if (!(await confirmReopen())) {
      return;
    }

    setConflict(false);
    rename.mutate(name.trim(), {
      onSuccess: (saved) => {
        setName(saved.name);
        announce('Kitap adı kaydedildi.');
      },
      onError: (error) => {
        setNameError(handleError(error));
      },
    });
  }

  async function saveOrder(paperUids: string[], { paper, position }: PaperMove) {
    setOrderError(undefined);
    if (!(await confirmReopen())) {
      return;
    }

    setConflict(false);
    reorder.mutate(paperUids, {
      onSuccess: () => {
        announce(`${paper.fileName}, ${String(position)}. sıraya taşındı.`);
      },
      onError: (error) => {
        const message = handleError(error);
        if (!isConflict(error)) {
          setOrderError(message);
        }
      },
    });
  }

  async function saveTitle(paper: Paper, title: string): Promise<TitleSaveResult> {
    if (title === paper.title) {
      return 'saved';
    }

    if (!(await confirmReopen())) {
      return 'cancelled';
    }

    setConflict(false);
    try {
      await setTitle.mutateAsync({ paperUid: paper.uid, title });
    } catch (error) {
      // Shown under the title box; a conflict gets the same wording as the alert at the top.
      throw new Error(handleError(error), { cause: error });
    }

    announce(`${paper.fileName} başlığı kaydedildi.`);
    return 'saved';
  }

  function start() {
    generate.mutate(undefined, {
      onSuccess: () => {
        announce('Kitap hazırlanmak üzere kuyruğa alındı.');
        void navigate(paths.book(book.uid));
      },
      onError: (error) => {
        handleError(error);
      },
    });
  }

  return (
    <div className="flex flex-col gap-6 pb-4">
      <div className="flex flex-col gap-3">
        {/* People come here from Kitaplarım; the book's own page is linked at the bottom ("Kitabı görüntüle"). */}
        <Link to={paths.library} className={buttonClasses('secondary', 'sm', 'w-fit')}>
          <ArrowLeftIcon size={16} aria-hidden="true" />
          Kitaplarım’a dön
        </Link>
        <div className="flex flex-col gap-1">
          <h1 className="text-3xl sm:text-4xl">Kitabı düzenle</h1>
          <p className="break-words text-ink-muted">{book.name}</p>
        </div>
      </div>

      {busy && (
        <Alert
          tone="info"
          title={BUSY_MESSAGE}
          action={
            <Link to={paths.book(book.uid)} className={buttonClasses('secondary', 'sm')}>
              Kitap sayfasına git
            </Link>
          }
        >
          Kitap kuyrukta veya hazırlanıyor. İşlem bitince buradan düzenleyebilirsiniz.
        </Alert>
      )}
      {book.status === 'Completed' && (
        <Alert tone="info" title="Kitap oluşturuldu">
          {COMPLETED_NOTICE}
        </Alert>
      )}
      {conflict && (
        <Alert tone="warning" title="Kitap değişti">
          {CONFLICT_MESSAGE}
        </Alert>
      )}
      {generate.isError && !isConflict(generate.error) && (
        <Alert tone="danger" title="Kitap oluşturma başlatılamadı">
          {errorMessage(generate.error)}
        </Alert>
      )}

      <Card className="p-5 sm:p-6">
        <form onSubmit={(event) => void saveName(event)} noValidate className="flex flex-col gap-4">
          <h2 className="text-2xl">Kitap adı</h2>
          <TextField
            label="Kitap adı"
            name="name"
            autoComplete="off"
            maxLength={BOOK_NAME_MAX}
            value={name}
            disabled={busy}
            error={nameError}
            hint="Kapakta, üst bilgide ve PDF dosya adında kullanılır."
            onChange={(event) => {
              setName(event.target.value);
              setNameError(undefined);
            }}
          />
          <div className="flex justify-end">
            <Button
              type="submit"
              variant="secondary"
              icon={<FloppyDiskIcon size={18} aria-hidden="true" />}
              loading={rename.isPending}
              disabled={busy}
            >
              Adı kaydet
            </Button>
          </div>
        </form>
      </Card>

      <Card className="flex flex-col gap-5 p-5 sm:p-6">
        <div className="flex flex-col gap-1">
          <h2 className="text-2xl">Bildiriler</h2>
          <p className="text-sm text-ink-muted">
            Sırayı Yukarı / Aşağı düğmeleriyle, bir başlığı yanındaki kalem düğmesiyle değiştirebilirsiniz.
            Her değişiklik hemen kaydedilir.
          </p>
        </div>
        {orderError && (
          <Alert tone="danger" title="Sıra kaydedilemedi">
            {orderError}
          </Alert>
        )}
        <PaperOrderList
          papers={book.papers}
          locked={busy || reorder.isPending}
          onReorder={(uids, move) => void saveOrder(uids, move)}
          onTitleSave={saveTitle}
        />
      </Card>

      <MissingTitlesAlert papers={book.papers} />
      <div className="flex flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-between">
        <Button
          variant="danger"
          icon={<TrashIcon size={18} aria-hidden="true" />}
          disabled={busy}
          onClick={() => {
            setDeleting(true);
          }}
        >
          Kitabı sil
        </Button>
        {book.status === 'Completed' ? (
          <Link to={paths.book(book.uid)} className={buttonClasses('primary')}>
            <BookOpenIcon size={18} aria-hidden="true" />
            Kitabı görüntüle
          </Link>
        ) : (
          !busy && (
            <Button variant="primary" loading={generate.isPending} onClick={start}>
              Kitabı Oluştur
            </Button>
          )
        )}
      </div>

      <Dialog
        open={confirming}
        title="PDF silinsin mi?"
        onClose={() => {
          answer(false);
        }}
        actions={
          <>
            <Button
              variant="secondary"
              onClick={() => {
                answer(false);
              }}
            >
              Vazgeç
            </Button>
            <Button
              variant="primary"
              onClick={() => {
                answer(true);
              }}
            >
              Devam et
            </Button>
          </>
        }
      >
        <p>{COMPLETED_NOTICE}</p>
      </Dialog>

      <DeleteBookDialog
        book={deleting ? book : undefined}
        onClose={() => {
          setDeleting(false);
        }}
        onDeleted={() => {
          void navigate(paths.library);
        }}
      />
    </div>
  );
}
