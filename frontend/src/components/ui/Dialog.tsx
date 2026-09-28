import { useEffect, useId, useRef, type ReactNode } from 'react';

export interface DialogProps {
  open: boolean;
  /** Called on Esc, on "outside" clicks (large dialogs) and by the caller's own buttons. */
  onClose: () => void;
  title: string;
  children?: ReactNode;
  /** Buttons at the bottom. */
  actions: ReactNode;
  /** `bottom` opens as a sheet from the bottom edge (phones). */
  placement?: 'center' | 'bottom';
  /**
   * `sm`: a short question; the body text describes the dialog and the first button gets the focus.
   * `lg`: a form of up to ~720 px (full screen below md); its body scrolls, the first field gets the focus and a
   * click outside the dialog counts as a close request.
   */
  size?: 'sm' | 'lg';
  /** `lg` only: content above the buttons that stays in place while the body scrolls (a progress bar). */
  footerExtra?: ReactNode;
}

const focusableSelector =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Modal dialog on the native `<dialog>` element: the page behind is inert, Esc asks to close it, Tab stays inside
 * and the focus returns to where it was when the dialog closes. Dialogs opened on top of each other must be
 * rendered side by side (not one inside the other), so each keeps its own keyboard handling.
 */
export function Dialog({
  open,
  onClose,
  title,
  children,
  actions,
  placement = 'center',
  size = 'sm',
  footerExtra,
}: DialogProps) {
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
    const initial = size === 'lg' ? `[data-dialog-body] ${focusableSelector}` : `footer ${focusableSelector}`;
    dialog.querySelector<HTMLElement>(initial)?.focus();
    return () => {
      dialog.removeEventListener('keydown', trapFocus);
      dialog.close();
      previous?.focus();
    };
  }, [open, size]);

  // A large dialog's content fills its box, so a click that lands on the <dialog> itself is on the backdrop. Esc is
  // the keyboard way to ask for the same thing (the "cancel" event above).
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  });
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog || !open || size !== 'lg') {
      return;
    }

    const onBackdrop = (event: MouseEvent) => {
      if (event.target === dialog) {
        onCloseRef.current();
      }
    };
    dialog.addEventListener('click', onBackdrop);
    return () => {
      dialog.removeEventListener('click', onBackdrop);
    };
  }, [open, size]);

  if (!open) {
    return null;
  }

  const large = size === 'lg';
  const frame =
    placement === 'bottom'
      ? 'mx-0 mt-auto mb-0 max-h-[85dvh] w-full max-w-none overflow-y-auto rounded-b-none '
      : large
        ? 'm-0 h-dvh max-h-none w-full max-w-none rounded-none md:m-auto md:h-auto md:max-h-[calc(100dvh-4rem)] md:w-[calc(100%-4rem)] md:max-w-[720px] md:rounded-(--radius) '
        : 'm-auto w-[calc(100%-2rem)] max-w-md ';

  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      aria-describedby={children && !large ? bodyId : undefined}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      className={
        frame +
        'rounded-(--radius) border border-line bg-surface p-0 text-ink shadow-(--shadow-raised) backdrop:bg-ink/40 ' +
        (large ? 'open:flex open:flex-col' : '')
      }
    >
      {large ? (
        <>
          <div className="border-b border-line px-5 py-4 sm:px-6">
            <h2 id={titleId} className="text-2xl">
              {title}
            </h2>
          </div>
          <div data-dialog-body="" className="min-h-0 flex-1 overflow-y-auto px-5 py-5 sm:px-6">
            {children}
          </div>
          <footer className="flex flex-col gap-3 border-t border-line px-5 py-4 sm:px-6">
            {footerExtra}
            <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">{actions}</div>
          </footer>
        </>
      ) : (
        <>
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
        </>
      )}
    </dialog>
  );
}
