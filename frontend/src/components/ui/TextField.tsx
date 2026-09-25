import { useId, type InputHTMLAttributes, type ReactNode } from 'react';

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string;
  hint?: ReactNode;
  error?: string | undefined;
  /** Shown at the right of the label, for example a character counter. */
  aside?: ReactNode;
}

export function TextField({ label, hint, error, aside, className = '', ...rest }: TextFieldProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [hint ? hintId : undefined, error ? errorId : undefined].filter(Boolean).join(' ');

  return (
    <div className={`flex flex-col gap-1.5 ${className}`}>
      <div className="flex items-baseline justify-between gap-3">
        <label htmlFor={id} className="text-sm font-medium text-ink">
          {label}
        </label>
        {aside}
      </div>
      <input
        id={id}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy || undefined}
        className={
          'min-h-11 w-full rounded-(--radius) border bg-surface px-3 text-base text-ink transition-colors duration-150 ' +
          'placeholder:text-ink-muted ' +
          (error ? 'border-danger' : 'border-ink-subtle hover:border-ink-muted')
        }
        {...rest}
      />
      {hint && (
        <p id={hintId} className="text-sm text-ink-muted">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="text-sm text-danger">
          {error}
        </p>
      )}
    </div>
  );
}
