import { InfoIcon } from '@phosphor-icons/react';
import { useEffect, useId, useRef, useState } from 'react';
import type { BookDetail } from '../../api/types';

/**
 * A small info button at the end of the book's status line. It opens a panel with how many e-mail addresses and
 * phone numbers were removed, in total and per paper. Disclosure pattern: the button toggles the panel, Esc or a
 * click outside closes it and the focus goes back to the button.
 */
export function CleanupInfo({ book }: { book: BookDetail }) {
  const [open, setOpen] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const panelId = useId();
  const emails = book.papers.reduce((sum, paper) => sum + paper.removedEmailCount, 0);
  const phones = book.papers.reduce((sum, paper) => sum + paper.removedPhoneCount, 0);

  useEffect(() => {
    if (!open) {
      return;
    }

    function onPointerDown(event: PointerEvent) {
      const target = event.target as Node;
      if (!panel.current?.contains(target) && !button.current?.contains(target)) {
        setOpen(false);
      }
    }

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false);
        button.current?.focus();
      }
    }

    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  return (
    <>
      <button
        ref={button}
        type="button"
        aria-label="Temizlenen iletişim bilgileri"
        title="Temizlenen iletişim bilgileri"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => {
          setOpen((value) => !value);
        }}
        className="-my-3 inline-flex size-11 items-center justify-center rounded-full align-middle text-ink-muted transition-colors duration-[130ms] hover:bg-line/60 hover:text-ink motion-reduce:transition-none"
      >
        <InfoIcon size={18} aria-hidden="true" />
      </button>
      <div
        ref={panel}
        id={panelId}
        hidden={!open}
        className="absolute top-full left-0 z-20 mt-1 w-full max-w-md rounded-(--radius) border border-line bg-surface p-4 text-sm text-ink shadow-(--shadow-raised)"
      >
        <p className="font-medium">
          <span className="numeric">{emails}</span> e-posta adresi ve{' '}
          <span className="numeric">{phones}</span> telefon numarası temizlendi
        </p>
        <table className="mt-3 w-full border-collapse">
          <caption className="sr-only">Bildiri bazında temizlenen iletişim bilgileri</caption>
          <thead>
            <tr className="border-b border-line text-left text-xs text-ink-muted">
              <th scope="col" className="py-1 pr-2 font-medium">
                Sıra
              </th>
              <th scope="col" className="py-1 pr-2 font-medium">
                Bildiri
              </th>
              <th scope="col" className="py-1 pr-2 text-right font-medium">
                E-posta
              </th>
              <th scope="col" className="py-1 text-right font-medium">
                Telefon
              </th>
            </tr>
          </thead>
          <tbody>
            {book.papers.map((paper) => (
              <tr key={paper.uid} className="border-b border-line last:border-b-0">
                <td className="numeric py-1 pr-2 text-ink-muted">{paper.order}</td>
                <td className="max-w-0 py-1 pr-2">
                  <span className="block truncate" title={paper.title}>
                    {paper.title}
                  </span>
                </td>
                <td className="numeric py-1 pr-2 text-right">{paper.removedEmailCount}</td>
                <td className="numeric py-1 text-right">{paper.removedPhoneCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
