import { CheckIcon } from '@phosphor-icons/react';

export interface StepperProps {
  steps: readonly string[];
  /** Zero-based index of the current step. */
  current: number;
}

/**
 * Wide screens: the numbered steps in a row. Phones: one line ("Adım 2 / 3 · Sıra ve kontrol") over a
 * three-part bar. The numbered list stays in the accessibility tree on every screen size (visually hidden on
 * phones) so `aria-current="step"` is always announced; the compact line is only visual.
 */
export function Stepper({ steps, current }: StepperProps) {
  const currentStep = steps[current];
  return (
    <nav aria-label="Adımlar">
      <div aria-hidden="true" className="flex flex-col gap-2 sm:hidden">
        <p className="text-sm text-ink-muted">
          Adım <span className="numeric">{current + 1}</span> /{' '}
          <span className="numeric">{steps.length}</span>
          {currentStep && (
            <>
              {' · '}
              <span className="font-medium text-ink">{currentStep}</span>
            </>
          )}
        </p>
        <div
          className="grid gap-1"
          style={{ gridTemplateColumns: `repeat(${steps.length}, minmax(0, 1fr))` }}
        >
          {steps.map((step, index) => (
            <span
              key={step}
              className={`h-1 rounded-full ${index <= current ? 'bg-accent' : 'bg-line-strong'}`}
            />
          ))}
        </div>
      </div>

      <ol className="sr-only flex-wrap items-center gap-x-3 gap-y-2 text-sm sm:not-sr-only sm:flex">
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
