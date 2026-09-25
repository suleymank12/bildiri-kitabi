import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Spinner } from './Spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'md' | 'sm';

const base =
  'inline-flex items-center justify-center gap-2 rounded-(--radius) border font-medium whitespace-nowrap ' +
  'transition-colors duration-150 select-none disabled:cursor-not-allowed disabled:opacity-55';

const variants: Record<ButtonVariant, string> = {
  primary:
    'border-accent bg-accent text-surface hover:enabled:border-accent-hover hover:enabled:bg-accent-hover',
  secondary:
    'border-ink-subtle bg-surface text-ink hover:enabled:border-ink-muted hover:enabled:bg-surface-muted',
  ghost:
    'border-transparent bg-transparent text-ink-muted hover:enabled:bg-surface-muted hover:enabled:text-ink',
  danger: 'border-danger bg-danger text-surface hover:enabled:opacity-90',
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
): string {
  return `${base} ${variants[variant]} ${sizes[size]} ${extra}`.trim();
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Shows a spinner and blocks further clicks. */
  loading?: boolean;
  icon?: ReactNode;
}

export function Button({
  variant = 'secondary',
  size = 'md',
  loading = false,
  icon,
  disabled,
  className = '',
  children,
  type = 'button',
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={buttonClasses(variant, size, className)}
      disabled={disabled === true || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {loading ? <Spinner /> : icon}
      {children}
    </button>
  );
}
