import { CheckCircleIcon, InfoIcon, WarningIcon, WarningOctagonIcon } from '@phosphor-icons/react';
import type { ReactNode } from 'react';

export type AlertTone = 'info' | 'success' | 'warning' | 'danger';

const tones: Record<AlertTone, { box: string; icon: ReactNode }> = {
  info: { box: 'border-accent/25 bg-accent-soft', icon: <InfoIcon size={20} className="text-accent" /> },
  success: {
    box: 'border-success/25 bg-success-soft',
    icon: <CheckCircleIcon size={20} className="text-success" />,
  },
  warning: {
    box: 'border-warning/25 bg-warning-soft',
    icon: <WarningIcon size={20} className="text-warning" />,
  },
  danger: {
    box: 'border-danger/25 bg-danger-soft',
    icon: <WarningOctagonIcon size={20} className="text-danger" />,
  },
};

export interface AlertProps {
  tone?: AlertTone;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
  className?: string;
}

/** A message box. Only errors interrupt screen readers (`role="alert"`); the others are read in place. */
export function Alert({ tone = 'info', title, children, action, className = '' }: AlertProps) {
  const style = tones[tone];
  return (
    <div
      role={tone === 'danger' ? 'alert' : undefined}
      className={`flex flex-col gap-3 rounded-(--radius) border px-4 py-3 text-ink sm:flex-row sm:items-start ${style.box} ${className}`}
    >
      <div className="flex flex-1 gap-3">
        <span aria-hidden="true" className="mt-0.5 shrink-0">
          {style.icon}
        </span>
        <div className="flex min-w-0 flex-col gap-1">
          <p className="font-medium">{title}</p>
          {children && <div className="text-sm text-ink-muted">{children}</div>}
        </div>
      </div>
      {action && <div className="shrink-0 sm:self-center">{action}</div>}
    </div>
  );
}
