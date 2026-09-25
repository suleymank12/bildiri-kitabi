import { CheckCircleIcon } from '@phosphor-icons/react';
import type { BookDetail } from '../../api/types';
import { formatInteger } from '../../lib/format';

/** One line once the book is ready: page count and how many contact details were kept out of it. */
export function CompletedSummary({ book }: { book: BookDetail }) {
  const removedEmails = book.papers.reduce((sum, paper) => sum + paper.removedEmailCount, 0);
  const removedPhones = book.papers.reduce((sum, paper) => sum + paper.removedPhoneCount, 0);

  return (
    <p className="flex items-start gap-2 rounded-(--radius) border border-success/25 bg-success-soft px-4 py-3 text-sm text-ink">
      <CheckCircleIcon size={18} aria-hidden="true" className="mt-px shrink-0 text-success" />
      <span>
        <span className="font-medium">Kitap hazır</span>
        {' · '}
        <span className="numeric">{formatInteger(book.pageCount ?? 0)}</span> sayfa{' · '}
        <span className="numeric">{removedEmails}</span> e-posta adresi ve{' '}
        <span className="numeric">{removedPhones}</span> telefon numarası kitaba aktarılmadı
      </span>
    </p>
  );
}
