import { SortAscendingIcon } from '@phosphor-icons/react';
import { useEffect, useState, type SubmitEvent } from 'react';
import { useNavigate } from 'react-router';
import { mapUploadErrors, type UploadErrors } from '../../api/errors';
import { useCreateBook } from '../../api/hooks';
import { useAnnounce } from '../../app/Announcer';
import { paths } from '../../app/paths';
import { usePageTitle } from '../../app/usePageTitle';
import { Alert, Button, Card, ProgressBar, Stepper, TextField, Tooltip } from '../../components/ui';
import {
  BOOK_NAME_MAX,
  REQUIRED_PAPER_COUNT,
  bookNameError,
  describeSelectionProblem,
  isSelectionReady,
  isSortedByName,
  selectionProblems,
} from '../../lib/files';
import { possessiveSuffix } from '../../lib/format';
import { BOOK_STEPS } from '../book/steps';
import { FileDropZone } from './FileDropZone';
import { SelectedFileList } from './SelectedFileList';
import { useFileSelection } from './useFileSelection';

const noServerErrors: UploadErrors = { files: new Map(), general: [] };

/** How long the "sorted" confirmation stays on screen. */
const SORT_NOTICE_MS = 2500;

export function NewBookPage() {
  usePageTitle('Yeni kitap');
  const navigate = useNavigate();
  const announce = useAnnounce();
  const selection = useFileSelection();
  const createBook = useCreateBook();
  const [name, setName] = useState('');
  const [nameTouched, setNameTouched] = useState(false);
  const [serverErrors, setServerErrors] = useState<UploadErrors>(noServerErrors);
  const [sortNotice, setSortNotice] = useState(false);

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
    isSelectionReady(files.map((f) => ({ name: f.file.name, size: f.file.size, hash: f.hash })));
  const uploading = createBook.isPending;
  const sortedByName = isSortedByName(files.map((f) => f.file.name));
  // The upload reports 100 % once the last byte is sent; the server still has to check the files.
  const checking = uploading && createBook.progress >= 100;

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
    createBook.mutate(
      { name: name.trim(), files: files.map((f) => f.file) },
      {
        onSuccess: (book) => {
          announce('Dosyalar yüklendi. Sıra ve kontrol adımına geçildi.');
          void navigate(paths.book(book.id));
        },
        onError: (error) => {
          const mapped = mapUploadErrors(error);
          setServerErrors(mapped);
          announce('Yükleme tamamlanamadı.');
        },
      },
    );
  }

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-8">
      <div className="flex flex-col gap-5">
        <Stepper steps={BOOK_STEPS} current={0} />
        <div className="flex flex-col gap-2">
          <h1 className="text-3xl sm:text-4xl">Yeni kitap</h1>
          <p className="max-w-2xl text-ink-muted">
            Kitap adını yazın ve {REQUIRED_PAPER_COUNT} bildiri dosyasını seçin. Sonraki adımda sırayı ve
            tespit edilen başlıkları kontrol edebilirsiniz. E-posta adresleri ve telefon numaraları kitaba
            eklenmeden temizlenir.
          </p>
        </div>
      </div>

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

      <Card className="p-5 sm:p-6">
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
            setName(event.target.value);
            if (serverErrors.name) {
              setServerErrors({ ...serverErrors, name: undefined });
            }
          }}
          onBlur={() => {
            setNameTouched(true);
          }}
        />
      </Card>

      <Card className="flex flex-col gap-5 p-5 sm:p-6">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div className="flex flex-col gap-1">
            <h2 className="text-2xl">Bildirileri yükleyin</h2>
            <p className="text-sm text-ink-muted">
              Bildiriler bu sırayla kitaba eklenir. Sonraki adımda Yukarı / Aşağı düğmeleriyle
              değiştirebilirsiniz.
            </p>
          </div>
          <p
            className={`text-sm font-medium ${count === REQUIRED_PAPER_COUNT ? 'text-success' : 'text-ink-muted'}`}
            aria-live="polite"
          >
            <span className="numeric">{REQUIRED_PAPER_COUNT}</span> dosyadan{' '}
            <span className="numeric">{count}</span>
            {possessiveSuffix(count)} seçildi
          </p>
        </div>

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
      </Card>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-end">
        {uploading && (
          // Two stages: the bytes going up, then the server checking the files and finding the titles.
          <div className="flex flex-1 flex-col gap-1.5">
            <div className="flex items-baseline justify-between gap-3 text-sm text-ink-muted">
              <p role="status">
                {checking
                  ? 'Dosyalar kontrol ediliyor ve başlıklar tespit ediliyor…'
                  : 'Dosyalar yükleniyor…'}
              </p>
              {!checking && <span className="numeric">%{createBook.progress}</span>}
            </div>
            <ProgressBar
              value={checking ? undefined : createBook.progress}
              label={checking ? 'Dosyalar kontrol ediliyor' : 'Yükleme ilerlemesi'}
            />
          </div>
        )}
        <Button type="submit" variant="primary" loading={uploading} disabled={!ready}>
          {uploading ? (checking ? 'Kontrol ediliyor' : 'Yükleniyor') : 'Yükle ve devam et'}
        </Button>
      </div>
    </form>
  );
}
