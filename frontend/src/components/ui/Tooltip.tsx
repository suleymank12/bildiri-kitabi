import {
  useEffect,
  useId,
  useRef,
  useState,
  type FocusEventHandler,
  type KeyboardEventHandler,
  type PointerEventHandler,
  type ReactNode,
} from 'react';

/** How long a tooltip opened by a tap stays on screen. */
export const TOUCH_TOOLTIP_MS = 3000;

export interface TooltipTriggerProps {
  'aria-describedby': string | undefined;
  onPointerEnter: PointerEventHandler<HTMLElement>;
  onPointerLeave: PointerEventHandler<HTMLElement>;
  onPointerDown: PointerEventHandler<HTMLElement>;
  onFocus: FocusEventHandler<HTMLElement>;
  onBlur: FocusEventHandler<HTMLElement>;
  onKeyDown: KeyboardEventHandler<HTMLElement>;
}

export interface TooltipProps {
  /** The tooltip text; also the trigger's accessible description. Without it no tooltip exists. */
  content: string | undefined;
  /** Which edge of the trigger the bubble lines up with; `end` keeps it on screen near the right edge. */
  align?: 'center' | 'start' | 'end';
  /** Renders the trigger with the props that connect it to the tooltip. */
  children: (trigger: TooltipTriggerProps) => ReactNode;
}

const alignments = {
  center: 'left-1/2 -translate-x-1/2',
  start: 'left-0',
  end: 'right-0',
};

/**
 * A small dark bubble above its trigger. Opens while the mouse is over the trigger and closes the moment it
 * leaves (the bubble itself ignores the pointer); opens on keyboard focus and closes on blur or Esc; on touch
 * screens a tap opens it for a few seconds or until the next tap elsewhere. The trigger stays in the same place
 * in the tree whether or not there is a tooltip, so it never loses focus when `content` comes and goes.
 */
export function Tooltip({ content, align = 'center', children }: TooltipProps) {
  const id = useId();
  const wrapper = useRef<HTMLSpanElement>(null);
  const [open, setOpen] = useState(false);
  const [openedByTouch, setOpenedByTouch] = useState(false);

  // No text, no tooltip: forget an open state so it does not come back with the next text.
  if (content === undefined && open) {
    setOpen(false);
  }

  const visible = open && content !== undefined;

  useEffect(() => {
    if (!visible) {
      return;
    }

    // Esc dismisses (WCAG 1.4.13); a tap or click outside closes a bubble opened by touch.
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    }

    function onPointerDown(event: PointerEvent) {
      if (!(event.target instanceof Node && wrapper.current?.contains(event.target))) {
        setOpen(false);
      }
    }

    document.addEventListener('keydown', onKeyDown);
    document.addEventListener('pointerdown', onPointerDown);
    const timer = openedByTouch
      ? window.setTimeout(() => {
          setOpen(false);
        }, TOUCH_TOOLTIP_MS)
      : undefined;
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.removeEventListener('pointerdown', onPointerDown);
      window.clearTimeout(timer);
    };
  }, [visible, openedByTouch]);

  function show(byTouch: boolean) {
    if (content !== undefined) {
      setOpenedByTouch(byTouch);
      setOpen(true);
    }
  }

  const trigger: TooltipTriggerProps = {
    'aria-describedby': content !== undefined ? id : undefined,
    onPointerEnter: (event) => {
      if (event.pointerType !== 'touch') {
        show(false);
      }
    },
    onPointerLeave: (event) => {
      if (event.pointerType !== 'touch') {
        setOpen(false);
      }
    },
    onPointerDown: (event) => {
      if (event.pointerType === 'touch') {
        show(true);
      }
    },
    onFocus: () => {
      // A tap focuses the button after its pointerdown; that already chose the touch behaviour.
      if (content !== undefined) {
        setOpen(true);
      }
    },
    onBlur: () => {
      setOpenedByTouch(false);
      setOpen(false);
    },
    onKeyDown: (event) => {
      if (event.key === 'Escape' && visible) {
        // Only the tooltip closes; a dialog around the trigger stays open.
        event.stopPropagation();
        setOpen(false);
      }
    },
  };

  return (
    <span ref={wrapper} className="relative inline-flex">
      {children(trigger)}
      {content !== undefined && (
        // Stays in the tree while hidden: it is the trigger's accessible description.
        <span
          id={id}
          role="tooltip"
          hidden={!visible}
          className="tooltip-pop pointer-events-none absolute inset-x-0 bottom-full z-30 h-2"
        >
          <span
            className={`absolute bottom-full w-max max-w-64 rounded-(--radius-sm) bg-ink px-2.5 py-1.5 text-xs leading-snug font-medium text-surface shadow-[0_4px_14px_rgb(28_34_48/0.18)] ${alignments[align]}`}
          >
            {content}
          </span>
          <span
            aria-hidden="true"
            className="absolute bottom-0.5 left-1/2 size-0 -translate-x-1/2 border-x-[6px] border-t-[6px] border-x-transparent border-t-ink"
          />
        </span>
      )}
    </span>
  );
}
