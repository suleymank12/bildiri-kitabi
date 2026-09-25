import { TrashIcon } from '@phosphor-icons/react';
import { Badge, Button, Spinner } from '../../components/ui';
import { describeIssue } from '../../lib/files';
import { formatBytes } from '../../lib/format';
import type { SelectedFile } from './useFileSelection';

export interface SelectedFileListProps {
  files: readonly SelectedFile[];
  /** Messages from the server, by file name. */
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
        className="hidden grid-cols-[2.5rem_minmax(0,1fr)_6rem_8rem_7rem] gap-4 border-b border-line px-3 pb-2 text-xs font-medium tracking-wide text-ink-muted uppercase md:grid"
      >
        <span>Sıra</span>
        <span>Dosya</span>
        <span className="text-right">Boyut</span>
        <span>Durum</span>
        <span />
      </div>
      <ol aria-label="Seçilen dosyalar" className="flex flex-col gap-2 md:gap-0">
        {files.map((file, index) => {
          const status = rowStatus(file, serverErrors.get(file.file.name));
          const messageId = `${file.id}-message`;
          return (
            <li
              key={file.id}
              aria-describedby={status.message ? messageId : undefined}
              className={
                'grid grid-cols-[2.5rem_minmax(0,1fr)] gap-x-3 gap-y-2 rounded-(--radius) border px-3 py-3 ' +
                'md:grid-cols-[2.5rem_minmax(0,1fr)_6rem_8rem_7rem] md:items-center md:gap-4 md:rounded-none md:border-0 md:border-b ' +
                (status.message
                  ? 'border-danger/40 bg-danger-soft/40 md:border-line'
                  : 'border-line bg-surface md:bg-transparent')
              }
            >
              <span className="numeric pt-0.5 text-sm text-ink-muted md:pt-0">
                {String(index + 1).padStart(2, '0')}
              </span>
              <div className="flex min-w-0 flex-col gap-1">
                <span className="font-medium break-all text-ink">{file.file.name}</span>
                {status.message && (
                  <span id={messageId} className="text-sm text-danger">
                    {status.message}
                  </span>
                )}
              </div>
              <span className="numeric col-start-2 text-sm text-ink-muted md:col-start-auto md:text-right">
                <span className="md:sr-only">Boyut: </span>
                {formatBytes(file.file.size)}
              </span>
              <span className="col-start-2 md:col-start-auto">{status.badge}</span>
              <div className="col-span-2 flex justify-end border-t border-line pt-2 md:col-span-1 md:border-0 md:pt-0">
                <Button
                  variant="ghost"
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
