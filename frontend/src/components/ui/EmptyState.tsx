import type { ReactNode } from 'react';

export interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
}

export function EmptyState({ icon, title, children, action }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-3 px-6 py-14 text-center">
      {icon && (
        <span aria-hidden="true" className="text-ink-subtle">
          {icon}
        </span>
      )}
      <h2 className="text-xl">{title}</h2>
      {children && <div className="max-w-md text-ink-muted">{children}</div>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}
