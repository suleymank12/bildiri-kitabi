import type { ReactNode } from 'react';

export interface EmptyStateProps {
  icon?: ReactNode;
  title: string;
  children?: ReactNode;
  action?: ReactNode;
  /** `h1` when the empty state is the whole page (not found, errors). */
  headingLevel?: 'h1' | 'h2';
}

export function EmptyState({ icon, title, children, action, headingLevel = 'h2' }: EmptyStateProps) {
  const Heading = headingLevel;
  return (
    <div className="flex flex-col items-center gap-3 px-6 py-14 text-center">
      {icon && (
        <span aria-hidden="true" className="text-ink-subtle">
          {icon}
        </span>
      )}
      <Heading className={headingLevel === 'h1' ? 'text-3xl' : 'text-xl'}>{title}</Heading>
      {children && <div className="max-w-md text-ink-muted">{children}</div>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  );
}
