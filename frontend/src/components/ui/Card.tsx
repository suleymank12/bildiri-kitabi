import type { HTMLAttributes } from 'react';

export function Card({ className = '', children, ...rest }: HTMLAttributes<HTMLElement>) {
  return (
    <section className={`rounded-(--radius) border border-line bg-surface ${className}`} {...rest}>
      {children}
    </section>
  );
}
