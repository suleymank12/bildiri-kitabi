import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react';
import { Spinner } from './Spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'danger-quiet';
export type ButtonSize = 'md' | 'sm';

// 130 ms colour transitions; none at all when the user asks for reduced motion.
const base =
  'inline-flex items-center justify-center gap-2 rounded-(--radius) border font-medium whitespace-nowrap ' +
  'transition-colors duration-[130ms] motion-reduce:transition-none select-none disabled:opacity-55';

// Look and interaction (hover, pressed, focus) are kept apart so a soft-disabled button keeps only the look.
const variants: Record<ButtonVariant, { look: string; interaction: string }> = {
  primary: {
    look: 'border-accent bg-accent text-surface',
    interaction:
      'hover:not-disabled:border-accent-hover hover:not-disabled:bg-accent-hover active:not-disabled:border-accent-active active:not-disabled:bg-accent-active',
  },
  secondary: {
    look: 'border-ink-subtle bg-surface text-ink',
    interaction:
      'hover:not-disabled:border-ink-muted hover:not-disabled:bg-accent-soft active:not-disabled:bg-line',
  },
  ghost: {
    look: 'border-transparent bg-transparent text-ink-muted',
    interaction:
      'hover:not-disabled:bg-accent-soft hover:not-disabled:text-ink active:not-disabled:bg-line active:not-disabled:text-ink',
  },
  danger: {
    look: 'border-danger bg-danger text-surface',
    interaction:
      'hover:not-disabled:border-danger-hover hover:not-disabled:bg-danger-hover active:not-disabled:border-danger-hover active:not-disabled:bg-danger-hover',
  },
  // Destructive actions inside lists ("Kaldır", "Sil"): neutral until pointed at or focused.
  'danger-quiet': {
    look: 'border-transparent bg-transparent text-ink-muted',
    interaction:
      'hover:not-disabled:bg-danger-soft hover:not-disabled:text-danger focus-visible:bg-danger-soft focus-visible:text-danger ' +
      'active:not-disabled:border-danger/30 active:not-disabled:bg-danger-soft active:not-disabled:text-danger',
  },
};

// Touch targets stay at least 44 px high on every screen size.
const sizes: Record<ButtonSize, string> = {
  md: 'min-h-11 px-4 text-[0.9375rem]',
  sm: 'min-h-11 px-3 text-sm',
};

/** The button look, for links that act as buttons. */
export function buttonClasses(
  variant: ButtonVariant = 'secondary',
  size: ButtonSize = 'md',
  extra = '',
  interactive = true,
): string {
  const { look, interaction } = variants[variant];
  return `${base} ${look} ${interactive ? interaction : 'opacity-55'} ${sizes[size]} ${extra}`.trim();
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Shows a spinner and blocks further clicks. */
  loading?: boolean;
  /**
   * Unavailable but still focusable (`aria-disabled`): for a button that becomes unavailable because of its own
   * click, so keyboard focus is not lost. Clicks are ignored.
   */
  softDisabled?: boolean;
  icon?: ReactNode;
  ref?: Ref<HTMLButtonElement>;
}

export function Button({
  variant = 'secondary',
  size = 'md',
  loading = false,
  softDisabled = false,
  icon,
  disabled,
  className = '',
  children,
  type = 'button',
  onClick,
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={buttonClasses(variant, size, className, !softDisabled)}
      disabled={disabled === true || loading}
      aria-disabled={softDisabled || undefined}
      aria-busy={loading || undefined}
      onClick={softDisabled ? undefined : onClick}
      {...rest}
    >
      {loading ? <Spinner /> : icon}
      {children}
    </button>
  );
}
