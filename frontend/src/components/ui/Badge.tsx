import type { ReactNode } from 'react';
import type { Tone } from '../../lib/status';

const tones: Record<Tone, string> = {
  neutral: 'border-line bg-surface-muted text-ink-muted',
  success: 'border-transparent bg-success-soft text-success',
  warning: 'border-transparent bg-warning-soft text-warning',
  danger: 'border-transparent bg-danger-soft text-danger',
  accent: 'border-transparent bg-accent-soft text-accent',
};

export function Badge({
  tone = 'neutral',
  icon,
  children,
}: {
  tone?: Tone;
  icon?: ReactNode;
  children: ReactNode;
}) {
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-(--radius-sm) border px-2 py-0.5 text-xs font-medium whitespace-nowrap ${tones[tone]}`}
    >
      {icon}
      {children}
    </span>
  );
}
