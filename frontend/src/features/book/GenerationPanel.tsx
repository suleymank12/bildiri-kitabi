import { ArrowSquareOutIcon, DownloadSimpleIcon } from '@phosphor-icons/react';
import { pdfUrl } from '../../api/hooks';
import type { BookDetail } from '../../api/types';
import { Card, ProgressBar, buttonClasses } from '../../components/ui';
import { formatBytes, formatDateTime, formatInteger } from '../../lib/format';
import { stageLabel } from '../../lib/status';

/** Progress while the book is queued or generated, the result when it is ready. */
export function GenerationPanel({ book }: { book: BookDetail }) {
  if (book.status === 'Completed') {
    return <CompletedPanel book={book} />;
  }

  const stage =
    book.status === 'Queued' ? 'Sırada bekliyor' : book.stage ? stageLabel(book.stage) : 'Başlatılıyor';
  return (
    <Card className="flex flex-col gap-5 p-5 sm:p-6">
      <div className="flex flex-col gap-1">
        <h2 className="text-2xl">Kitap hazırlanıyor</h2>
        <p className="text-sm text-ink-muted">Bu sayfadan ayrılabilirsiniz; hazırlık sunucuda sürer.</p>
      </div>
      <div className="flex flex-col gap-2">
        <div className="flex items-baseline justify-between gap-4">
          <span className="font-medium text-ink">{stage}</span>
          <span className="numeric text-sm text-ink-muted">%{book.progressPercent}</span>
        </div>
        <ProgressBar
          value={book.progressPercent}
          label="Kitap hazırlama ilerlemesi"
          valueText={`${stage}, yüzde ${book.progressPercent}`}
        />
      </div>
    </Card>
  );
}

function CompletedPanel({ book }: { book: BookDetail }) {
  const removedEmails = book.papers.reduce((sum, paper) => sum + paper.removedEmailCount, 0);
  const removedPhones = book.papers.reduce((sum, paper) => sum + paper.removedPhoneCount, 0);
  const open = pdfUrl(book);
  const download = pdfUrl(book, true);

  return (
    <Card className="flex flex-col gap-6 p-5 sm:p-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="flex flex-col gap-1">
          <h2 className="text-2xl">Kitap hazır</h2>
          <p className="text-sm text-ink-muted">
            <span className="numeric">{formatInteger(book.pageCount ?? 0)}</span> sayfa
            {book.pdfSizeBytes != null && (
              <>
                {' · '}
                <span className="numeric">{formatBytes(book.pdfSizeBytes)}</span>
              </>
            )}
            {book.processingFinishedAt && <> · {formatDateTime(book.processingFinishedAt)}</>}
          </p>
          <p className="text-sm text-ink-muted">
            <span className="numeric">{removedEmails}</span> e-posta adresi ve{' '}
            <span className="numeric">{removedPhones}</span> telefon numarası kitaba aktarılmadı.
          </p>
        </div>
        {open && download && (
          <div className="flex flex-col gap-2 sm:flex-row">
            <a href={open} target="_blank" rel="noopener" className={buttonClasses('primary')}>
              <ArrowSquareOutIcon size={18} aria-hidden="true" />
              PDF’i aç
              <span className="sr-only"> (yeni sekmede)</span>
            </a>
            <a href={download} className={buttonClasses('secondary')}>
              <DownloadSimpleIcon size={18} aria-hidden="true" />
              İndir
            </a>
          </div>
        )}
      </div>

      <div className="flex flex-col gap-2">
        <h3 className="text-lg">İçindekiler</h3>
        <ol className="flex flex-col">
          {book.papers.map((paper) => (
            <li
              key={paper.id}
              className="grid grid-cols-[2.5ch_minmax(0,1fr)_auto] gap-x-3 border-b border-line py-2 last:border-b-0"
            >
              <span className="numeric text-sm text-ink-muted">{paper.order}.</span>
              <span className="font-serif text-[0.9375rem] leading-snug text-ink">{paper.title}</span>
              <span className="numeric text-right text-sm text-ink-muted">
                <span className="sr-only">Sayfa </span>
                {paper.startPage ?? '–'}
              </span>
            </li>
          ))}
        </ol>
      </div>
    </Card>
  );
}
