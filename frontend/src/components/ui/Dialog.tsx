import { useEffect, useId, useRef, type ReactNode } from 'react';

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title: string;
  children?: ReactNode;
  /** Buttons at the bottom; the first focusable element gets the focus when the dialog opens. */
  actions: ReactNode;
}

const focusableSelector =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Modal dialog on the native `<dialog>` element: the page behind is inert, Esc closes it, Tab stays inside and the
 * focus returns to where it was when the dialog closes.
 */
export function Dialog({ open, onClose, title, children, actions }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const bodyId = useId();

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog || !open) {
      return;
    }

    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const trapFocus = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') {
        return;
      }

      const items = Array.from(dialog.querySelectorAll<HTMLElement>(focusableSelector));
      const first = items[0];
      const last = items.at(-1);
      if (!first || !last) {
        return;
      }

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    dialog.addEventListener('keydown', trapFocus);
    dialog.showModal();
    dialog.querySelector<HTMLElement>(`footer ${focusableSelector}`)?.focus();
    return () => {
      dialog.removeEventListener('keydown', trapFocus);
      dialog.close();
      previous?.focus();
    };
  }, [open]);

  if (!open) {
    return null;
  }

  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      aria-describedby={children ? bodyId : undefined}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className="m-auto w-[calc(100%-2rem)] max-w-md rounded-(--radius) border border-line bg-surface p-0 text-ink shadow-(--shadow-raised) backdrop:bg-ink/40"
    >
      <div className="flex flex-col gap-3 px-6 pt-6">
        <h2 id={titleId} className="text-xl">
          {title}
        </h2>
        {children && (
          <div id={bodyId} className="text-ink-muted">
            {children}
          </div>
        )}
      </div>
      <footer className="flex flex-col-reverse gap-2 px-6 pt-5 pb-6 sm:flex-row sm:justify-end">
        {actions}
      </footer>
    </dialog>
  );
}
