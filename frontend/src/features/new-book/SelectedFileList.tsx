import { TrashIcon } from '@phosphor-icons/react';
import { Badge, Button, Spinner } from '../../components/ui';
import { BreakableFileName } from '../../lib/fileName';
import { describeIssue } from '../../lib/files';
import { formatBytes } from '../../lib/format';
import { FILE_TABLE_COLUMNS, NUMERIC_COLUMN } from '../../lib/tableColumns';
import type { SelectedFile } from './useFileSelection';

export interface SelectedFileListProps {
  files: readonly SelectedFile[];
  /** Messages from the server, by the file's selection id ({@link SelectedFile.id}). */
  serverErrors: ReadonlyMap<string, string>;
  onRemove: (id: string) => void;
  disabled?: boolean;
}

function rowStatus(file: SelectedFile, serverError: string | undefined) {
  if (file.issue) {
    return { badge: <Badge tone="danger">Hatalı</Badge>, message: describeIssue(file.issue) };
  }

  if (serverError) {
    return { badge: <Badge tone="danger">Reddedildi</Badge>, message: serverError };
  }

  if (file.checking) {
    return {
      badge: <Badge icon={<Spinner className="size-3" />}>Denetleniyor</Badge>,
      message: undefined,
    };
  }

  return { badge: <Badge tone="success">Uygun</Badge>, message: undefined };
}

/** Table layout on wide screens, one card per file on phones. */
export function SelectedFileList({ files, serverErrors, onRemove, disabled = false }: SelectedFileListProps) {
  return (
    <div className="flex flex-col gap-2 md:gap-0">
      <div
        aria-hidden="true"
        className={`hidden border-b border-line px-3 pb-2 text-xs font-medium tracking-wide text-ink-muted uppercase md:grid ${FILE_TABLE_COLUMNS}`}
      >
        <span>Sıra</span>
        <span>Dosya</span>
        <span className={NUMERIC_COLUMN}>Boyut</span>
        <span>Durum</span>
        <span />
      </div>
      <ol aria-label="Seçilen dosyalar" className="flex flex-col gap-2 md:gap-0">
        {files.map((file, index) => {
          const status = rowStatus(file, serverErrors.get(file.id));
          const messageId = `${file.id}-message`;
          return (
            <li
              key={file.id}
              aria-describedby={status.message ? messageId : undefined}
              className={
                'grid grid-cols-[2.5rem_minmax(0,1fr)] gap-x-3 gap-y-2 rounded-(--radius) border px-3 py-3 ' +
                `${FILE_TABLE_COLUMNS} md:items-center md:rounded-none md:border-0 md:border-b ` +
                (status.message
                  ? 'border-danger/40 bg-danger-soft/40 md:border-line'
                  : 'border-line bg-surface md:bg-transparent')
              }
            >
              <span className="numeric pt-0.5 text-sm text-ink-muted md:pt-0">
                {String(index + 1).padStart(2, '0')}
              </span>
              <div className="flex min-w-0 flex-col gap-1">
                <span className="font-medium wrap-break-word text-ink">
                  <BreakableFileName name={file.file.name} />
                </span>
                {status.message && (
                  <span id={messageId} className="text-sm text-danger">
                    {status.message}
                  </span>
                )}
              </div>
              <span
                className={`numeric col-start-2 text-sm text-ink-muted md:col-start-auto ${NUMERIC_COLUMN}`}
              >
                <span className="md:sr-only">Boyut: </span>
                {formatBytes(file.file.size)}
              </span>
              <span className="col-start-2 md:col-start-auto">{status.badge}</span>
              <div className="col-span-2 flex justify-end border-t border-line pt-2 md:col-span-1 md:border-0 md:pt-0">
                <Button
                  variant="danger-quiet"
                  size="sm"
                  icon={<TrashIcon size={16} aria-hidden="true" />}
                  disabled={disabled}
                  aria-label={`${file.file.name} dosyasını kaldır`}
                  onClick={() => {
                    onRemove(file.id);
                  }}
                >
                  Kaldır
                </Button>
              </div>
            </li>
          );
        })}
      </ol>
    </div>
  );
}
