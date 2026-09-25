import { CheckIcon } from '@phosphor-icons/react';

export interface StepperProps {
  steps: readonly string[];
  /** Zero-based index of the current step. */
  current: number;
}

export function Stepper({ steps, current }: StepperProps) {
  return (
    <nav aria-label="Adımlar">
      <ol className="flex flex-wrap items-center gap-x-3 gap-y-2 text-sm">
        {steps.map((step, index) => {
          const done = index < current;
          const active = index === current;
          return (
            <li key={step} className="flex items-center gap-3" aria-current={active ? 'step' : undefined}>
              <span className="flex items-center gap-2">
                <span
                  aria-hidden="true"
                  className={
                    'numeric flex size-7 items-center justify-center rounded-full border text-xs ' +
                    (active
                      ? 'border-accent bg-accent text-surface'
                      : done
                        ? 'border-accent bg-accent-soft text-accent'
                        : 'border-ink-subtle bg-surface text-ink-muted')
                  }
                >
                  {done ? <CheckIcon size={14} /> : index + 1}
                </span>
                <span className={active ? 'font-medium text-ink' : 'text-ink-muted'}>
                  <span className="sr-only">{`${index + 1}. adım: `}</span>
                  {step}
                  {done && <span className="sr-only"> (tamamlandı)</span>}
                </span>
              </span>
              {index < steps.length - 1 && (
                <span aria-hidden="true" className="hidden h-px w-8 bg-line-strong sm:block" />
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
