import { SortAscendingIcon, UploadSimpleIcon } from '@phosphor-icons/react';
import { useEffect, useId, useRef, useState, type SubmitEvent } from 'react';
import { useNavigate } from 'react-router';
import { mapUploadErrors, type UploadErrors } from '../../api/errors';
import { useCreateBook } from '../../api/hooks';
import { useAnnounce } from '../../app/Announcer';
import { paths } from '../../app/paths';
import { Alert, Button, Dialog, ProgressBar, TextField, Tooltip } from '../../components/ui';
import {
  bookNameError,
  describeSelectionProblem,
  isSelectionReady,
  isSortedByName,
  selectionProblems,
} from '../../lib/files';
import { BOOK_NAME_MAX, REQUIRED_PAPER_COUNT } from '../../lib/limits';
import { possessiveSuffix } from '../../lib/format';
import { FileDropZone } from './FileDropZone';
import { SelectedFileList } from './SelectedFileList';
import { useFileSelection } from './useFileSelection';

const noServerErrors: UploadErrors = { files: new Map(), general: [] };

/** How long the "sorted" confirmation stays on screen. */
const SORT_NOTICE_MS = 2500;

/** Once shown, the upload status stays at least this long, so a fast local upload does not just flicker. */
export const MIN_STATUS_MS = 600;

/** Wall-clock time for the status panel's minimum; read only from event handlers. */
function now(): number {
  return Date.now();
}

export const DISCARD_QUESTION = 'Seçtiğiniz dosyalar ve yazdığınız ad silinecek. Kapatılsın mı?';

export interface NewBookDialogProps {
  open: boolean;
  onClose: () => void;
}

/**
 * "Yeni kitap": the book name and the ten papers in a modal over Kitaplarım. After a successful upload the modal
 * closes and the book page opens at "Sıra ve kontrol". The form lives only while the modal is open, so closing it
 * forgets everything; that is why a filled-in form asks before closing, and an upload in progress cannot be closed.
 */
export function NewBookDialog({ open, onClose }: NewBookDialogProps) {
  return open ? <NewBookForm onClose={onClose} /> : null;
}

function NewBookForm({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const announce = useAnnounce();
  const selection = useFileSelection();
  const createBook = useCreateBook();
  const formId = useId();
  const [name, setName] = useState('');
  // The name's error shows once the user typed in the field and left it, or pressed "Yükle ve devam et". A blur alone
  // does not count: the focus is also moved by code (opening and closing dialogs), not only by the user.
  const [nameTouched, setNameTouched] = useState(false);
  const nameEdited = useRef(false);
  const [serverErrors, setServerErrors] = useState<UploadErrors>(noServerErrors);
  const [sortNotice, setSortNotice] = useState(false);
  const [confirmingClose, setConfirmingClose] = useState(false);
  // The status panel replaces the form from "Yükle ve devam et" until the result has been shown long enough.
  const [showingStatus, setShowingStatus] = useState(false);
  const statusShownAt = useRef(0);
  const statusTimer = useRef<number>(undefined);
  // The panel takes the height the form showed (the visible part of a long form), so the modal does not jump when
  // the upload starts and the panel stays in view.
  const form = useRef<HTMLFormElement>(null);
  const [statusHeight, setStatusHeight] = useState<number>();

  useEffect(
    () => () => {
      window.clearTimeout(statusTimer.current);
    },
    [],
  );

  useEffect(() => {
    if (!sortNotice) {
      return;
    }

    const timer = window.setTimeout(() => {
      setSortNotice(false);
    }, SORT_NOTICE_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [sortNotice]);

  const nameError = bookNameError(name);
  const shownNameError = serverErrors.name ?? (nameTouched ? nameError : undefined);
  const files = selection.files;
  const count = files.length;
  const tooMany = count > REQUIRED_PAPER_COUNT;
  const totalProblem = selectionProblems(files.map((f) => ({ name: f.file.name, size: f.file.size }))).find(
    (problem) => problem.kind === 'totalTooLarge',
  );
  const ready =
    nameError === undefined &&
    isSelectionReady(files.map((f) => ({ name: f.file.name, size: f.file.size, hash: f.hash, zip: f.zip })));
  const uploading = createBook.isPending || showingStatus;
  const sortedByName = isSortedByName(files.map((f) => f.file.name));
  // The upload reports 100 % once the last byte is sent; the server still has to check the files.
  const checking = uploading && (!createBook.isPending || createBook.progress >= 100);
  const dirty = name.trim() !== '' || count > 0;

  function requestClose() {
    if (uploading) {
      return;
    }

    if (dirty) {
      setConfirmingClose(true);
    } else {
      onClose();
    }
  }

  function clearServerErrors() {
    if (serverErrors !== noServerErrors) {
      setServerErrors(noServerErrors);
    }
  }

  function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    setNameTouched(true);
    if (!ready || uploading) {
      return;
    }

    clearServerErrors();
    setStatusHeight(visibleFormHeight(form.current));
    setShowingStatus(true);
    statusShownAt.current = now();
    createBook.mutate(
      { name: name.trim(), files: files.map((f) => f.file) },
      {
        onSuccess: (book) => {
          afterMinimumStatusTime(() => {
            announce('Dosyalar yüklendi. Sıra ve kontrol adımına geçildi.');
            onClose();
            void navigate(paths.book(book.uid));
          });
        },
        onError: (error) => {
          afterMinimumStatusTime(() => {
            // The form comes back as it was, with the server's messages on its fields and rows.
            setServerErrors(mapUploadErrors(error));
            setShowingStatus(false);
            announce('Yükleme tamamlanamadı.');
          });
        },
      },
    );
  }

  function afterMinimumStatusTime(action: () => void) {
    const wait = Math.max(0, MIN_STATUS_MS - (now() - statusShownAt.current));
    statusTimer.current = window.setTimeout(action, wait);
  }

  return (
    <>
      <Dialog
        open
        size="lg"
        title="Yeni kitap"
        closeDisabled={uploading}
        onClose={requestClose}
        actions={
          <>
            <Button variant="secondary" disabled={uploading} onClick={requestClose}>
              Vazgeç
            </Button>
            <Button type="submit" form={formId} variant="primary" loading={uploading} disabled={!ready}>
              {uploading ? (checking ? 'Kontrol ediliyor' : 'Yükleniyor') : 'Yükle ve devam et'}
            </Button>
          </>
        }
      >
        {showingStatus && (
          <UploadStatus checking={checking} progress={createBook.progress} height={statusHeight} />
        )}
        <form
          ref={form}
          id={formId}
          onSubmit={submit}
          noValidate
          hidden={showingStatus}
          className="flex flex-col gap-6"
        >
          <p className="text-ink-muted">
            Kitap adını yazın ve {REQUIRED_PAPER_COUNT} bildiri dosyasını seçin. Sonraki adımda sırayı ve
            tespit edilen başlıkları kontrol edebilirsiniz. E-posta adresleri ve telefon numaraları kitaba
            eklenmeden temizlenir.
          </p>

          <TextField
            label="Kitap adı"
            name="name"
            autoComplete="off"
            required
            maxLength={BOOK_NAME_MAX}
            value={name}
            disabled={uploading}
            error={shownNameError}
            hint="Kapakta, üst bilgide ve PDF dosya adında kullanılır."
            aside={
              <span className="numeric text-xs text-ink-muted" aria-hidden="true">
                {name.trim().length}/{BOOK_NAME_MAX}
              </span>
            }
            onChange={(event) => {
              nameEdited.current = true;
              setName(event.target.value);
              if (serverErrors.name) {
                setServerErrors({ ...serverErrors, name: undefined });
              }
            }}
            onBlur={() => {
              if (nameEdited.current) {
                setNameTouched(true);
              }
            }}
          />

          {serverErrors.general.length > 0 && (
            <Alert tone="danger" title="Yükleme tamamlanamadı">
              <ul className="flex flex-col gap-1">
                {serverErrors.general.map((message) => (
                  <li key={message}>{message}</li>
                ))}
              </ul>
            </Alert>
          )}
          {tooMany && (
            <Alert tone="warning" title="Fazla dosya seçildi">
              {describeSelectionProblem({ kind: 'count', selected: count })}
            </Alert>
          )}
          {totalProblem && (
            <Alert tone="warning" title="Toplam boyut sınırı aşıldı">
              {describeSelectionProblem(totalProblem)}
            </Alert>
          )}

          <section aria-labelledby={`${formId}-papers`} className="flex flex-col gap-4">
            <div className="flex flex-col gap-1 sm:flex-row sm:items-end sm:justify-between sm:gap-3">
              <h3 id={`${formId}-papers`} className="text-xl">
                Bildiriler
              </h3>
              <p
                className={`text-sm font-medium ${count === REQUIRED_PAPER_COUNT ? 'text-success' : 'text-ink-muted'}`}
                aria-live="polite"
              >
                <span className="numeric">{REQUIRED_PAPER_COUNT}</span> dosyadan{' '}
                <span className="numeric">{count}</span>
                {possessiveSuffix(count)} seçildi
              </p>
            </div>
            <p className="text-sm text-ink-muted">
              Bildiriler bu sırayla kitaba eklenir. Sonraki adımda Yukarı / Aşağı düğmeleriyle
              değiştirebilirsiniz.
            </p>

            <FileDropZone
              disabled={uploading}
              onFiles={(added) => {
                clearServerErrors();
                selection.add(added);
              }}
            />

            {count > 0 && (
              <>
                <div className="flex flex-wrap items-center justify-end gap-x-3 gap-y-1">
                  {/* The confirmation is read out; the "already sorted" tooltip only describes the button. */}
                  <p role="status" className="text-sm font-medium text-success">
                    {sortNotice ? 'Dosyalar ada göre sıralandı.' : ''}
                  </p>
                  <Tooltip
                    align="end"
                    content={
                      !sortNotice && sortedByName && count > 1 ? 'Dosyalar zaten ada göre sıralı' : undefined
                    }
                  >
                    {(tooltip) => (
                      <Button
                        variant="ghost"
                        size="sm"
                        icon={<SortAscendingIcon size={16} aria-hidden="true" />}
                        disabled={uploading || count < 2}
                        softDisabled={sortedByName}
                        {...tooltip}
                        onClick={() => {
                          clearServerErrors();
                          selection.sortByName();
                          setSortNotice(true);
                        }}
                      >
                        Ada göre sırala
                      </Button>
                    )}
                  </Tooltip>
                </div>
                <SelectedFileList
                  files={files}
                  serverErrors={serverErrors.files}
                  disabled={uploading}
                  onRemove={(id) => {
                    clearServerErrors();
                    selection.remove(id);
                  }}
                />
              </>
            )}
          </section>
        </form>
      </Dialog>

      {/* Beside the form dialog, not inside it: each native dialog keeps its own Esc and Tab handling. */}
      <Dialog
        open={confirmingClose}
        title="Yeni kitap kapatılsın mı?"
        onClose={() => {
          setConfirmingClose(false);
        }}
        actions={
          <>
            <Button
              variant="secondary"
              onClick={() => {
                setConfirmingClose(false);
              }}
            >
              Hayır
            </Button>
            <Button variant="danger" onClick={onClose}>
              Evet
            </Button>
          </>
        }
      >
        <p>{DISCARD_QUESTION}</p>
      </Dialog>
    </>
  );
}

/** Height of the part of the form the dialog body shows: the whole form, or the body's inner height if it scrolls. */
function visibleFormHeight(form: HTMLFormElement | null): number | undefined {
  const body = form?.closest<HTMLElement>('[data-dialog-body]');
  if (!form || !body) {
    return undefined;
  }

  const style = getComputedStyle(body);
  const inner =
    body.clientHeight - Number.parseFloat(style.paddingTop) - Number.parseFloat(style.paddingBottom);
  return Math.min(form.offsetHeight, inner);
}

/**
 * Two stages: the bytes going up (with the percentage), then the server checking the files and finding the titles
 * (no percentage is known, so the bar moves on its own).
 */
function UploadStatus({
  checking,
  progress,
  height,
}: {
  checking: boolean;
  progress: number;
  height?: number | undefined;
}) {
  return (
    <div
      className="flex min-h-64 flex-col items-center justify-center gap-4 py-8 text-center"
      style={height === undefined ? undefined : { minHeight: height }}
    >
      <span aria-hidden="true" className="text-accent">
        <UploadSimpleIcon size={40} />
      </span>
      <p role="status" className="text-lg text-ink">
        {checking ? (
          'Dosyalar kontrol ediliyor ve başlıklar tespit ediliyor…'
        ) : (
          <>
            Dosyalar yükleniyor… <span className="numeric">%{progress}</span>
          </>
        )}
      </p>
      <div className="w-full max-w-md">
        <ProgressBar
          value={checking ? undefined : progress}
          label={checking ? 'Dosyalar kontrol ediliyor' : 'Yükleme ilerlemesi'}
        />
      </div>
    </div>
  );
}
