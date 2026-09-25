import { DotsThreeIcon } from '@phosphor-icons/react';
import { useEffect, useId, useRef, useState, type ReactNode } from 'react';

export interface RowMenuItem {
  label: string;
  onSelect: () => void;
  disabled?: boolean;
  /** Why the item is disabled; shown under it and as its tooltip. */
  disabledReason?: string;
  icon?: ReactNode;
  tone?: 'default' | 'danger';
}

/**
 * A small actions menu for a list row: button with `aria-haspopup`, `role="menu"` list, arrow keys between items,
 * Esc or a click outside closes it and returns the focus to the button.
 */
export function RowMenu({ label, items }: { label: string; items: readonly RowMenuItem[] }) {
  const [open, setOpen] = useState(false);
  const button = useRef<HTMLButtonElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const menuId = useId();

  useEffect(() => {
    if (!open) {
      return;
    }

    menu.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus();
    function onPointerDown(event: PointerEvent) {
      const target = event.target as Node;
      if (!menu.current?.contains(target) && !button.current?.contains(target)) {
        setOpen(false);
      }
    }

    document.addEventListener('pointerdown', onPointerDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
    };
  }, [open]);

  function close() {
    setOpen(false);
    button.current?.focus();
  }

  function onMenuKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    const menuItems = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('[role="menuitem"]'));
    const index = menuItems.indexOf(document.activeElement as HTMLElement);
    if (event.key === 'Escape' || event.key === 'Tab') {
      event.preventDefault();
      close();
    } else if (event.key === 'ArrowDown') {
      event.preventDefault();
      menuItems[(index + 1) % menuItems.length]?.focus();
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      menuItems[(index - 1 + menuItems.length) % menuItems.length]?.focus();
    }
  }

  return (
    <div className="relative">
      <button
        ref={button}
        type="button"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => {
          setOpen((value) => !value);
        }}
        className="flex size-11 items-center justify-center rounded-(--radius) text-ink-muted hover:bg-surface-muted hover:text-ink"
      >
        <DotsThreeIcon size={22} aria-hidden="true" />
      </button>
      {open && (
        <div
          ref={menu}
          id={menuId}
          role="menu"
          aria-label={label}
          tabIndex={-1}
          onKeyDown={onMenuKeyDown}
          className="absolute right-0 z-20 mt-1 flex w-64 flex-col rounded-(--radius) border border-line bg-surface py-1 shadow-(--shadow-raised)"
        >
          {items.map((item) => (
            <div key={item.label} className="flex flex-col">
              <button
                type="button"
                role="menuitem"
                aria-disabled={item.disabled || undefined}
                title={item.disabled ? item.disabledReason : undefined}
                onClick={() => {
                  if (!item.disabled) {
                    setOpen(false);
                    item.onSelect();
                  }
                }}
                className={
                  'flex min-h-11 items-center gap-2 px-3 text-left text-sm ' +
                  (item.disabled
                    ? 'cursor-not-allowed text-ink-muted'
                    : item.tone === 'danger'
                      ? 'text-danger hover:bg-danger-soft'
                      : 'text-ink hover:bg-surface-muted')
                }
              >
                {item.icon}
                {item.label}
              </button>
              {item.disabled && item.disabledReason && (
                <p className="px-3 pb-2 text-xs text-ink-muted">{item.disabledReason}</p>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
