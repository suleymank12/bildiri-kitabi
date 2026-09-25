import { CheckIcon, CircleIcon, XIcon } from '@phosphor-icons/react';
import { useEffect, useState } from 'react';
import type { BookDetail } from '../../../api/types';
import { Card, ProgressBar, Spinner } from '../../../components/ui';
import { formatDuration } from '../../../lib/format';
import { QUEUED_LABEL } from '../../../lib/status';
import { BookIllustration } from './BookIllustration';
import { stageSteps, type StageStep } from './stages';

/** Current time, refreshed every second while `active`. */
function useNow(active: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) {
      return;
    }

    const timer = window.setInterval(() => {
      setNow(Date.now());
    }, 1000);
    return () => {
      window.clearInterval(timer);
    };
  }, [active]);
  return now;
}

/** The waiting screen: real server stages, progress, elapsed time and a small animation. No simulated delay. */
export function GenerationProgress({ book }: { book: BookDetail }) {
  const steps = stageSteps(book);
  const current = steps.find((step) => step.state === 'current');
  const now = useNow(book.processingStartedAt != null);
  const elapsed =
    book.processingStartedAt != null
      ? formatDuration((now - new Date(book.processingStartedAt).getTime()) / 1000)
      : undefined;

  return (
    <Card className="flex flex-col gap-6 p-5 sm:p-6">
      <div className="flex flex-col items-start gap-5 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-col gap-1">
          <h2 className="text-2xl">Kitap hazırlanıyor</h2>
          <p className="text-sm text-ink-muted">
            Bu sayfadan ayrılabilirsiniz; hazırlık sunucuda sürer ve kaldığınız yerden izleyebilirsiniz.
          </p>
        </div>
        <BookIllustration />
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex items-baseline justify-between gap-4 text-sm">
          <span className="font-medium text-ink">{current?.label ?? QUEUED_LABEL}</span>
          <span className="text-ink-muted">
            <span className="numeric">%{book.progressPercent}</span>
            {elapsed && (
              <>
                {' · '}
                <span className="numeric">{elapsed}</span>
              </>
            )}
          </span>
        </div>
        <ProgressBar
          value={book.progressPercent}
          label="Kitap hazırlama ilerlemesi"
          valueText={`${current?.label ?? QUEUED_LABEL}, yüzde ${String(book.progressPercent)}`}
        />
      </div>

      <StageList steps={steps} />
    </Card>
  );
}

const stepText: Record<StageStep['state'], string> = {
  done: 'tamamlandı',
  current: 'sürüyor',
  pending: 'bekliyor',
  failed: 'hata',
};

export function StageList({ steps }: { steps: readonly StageStep[] }) {
  return (
    <ol aria-label="Aşamalar" className="flex flex-col gap-3">
      {steps.map((step) => (
        <li
          key={step.key}
          data-state={step.state}
          aria-current={step.state === 'current' ? 'step' : undefined}
          className={
            'flex items-center gap-3 text-[0.9375rem] ' +
            (step.state === 'pending'
              ? 'text-ink-muted'
              : step.state === 'failed'
                ? 'font-medium text-danger'
                : step.state === 'current'
                  ? 'font-medium text-ink'
                  : 'text-ink')
          }
        >
          <span aria-hidden="true" className="flex size-6 shrink-0 items-center justify-center">
            {step.state === 'done' && (
              <span className="flex size-6 items-center justify-center rounded-full bg-accent-soft text-accent">
                <CheckIcon size={14} />
              </span>
            )}
            {step.state === 'current' && <Spinner className="size-5 text-accent" />}
            {step.state === 'pending' && <CircleIcon size={18} className="text-ink-subtle" />}
            {step.state === 'failed' && (
              <span className="flex size-6 items-center justify-center rounded-full bg-danger-soft text-danger">
                <XIcon size={14} />
              </span>
            )}
          </span>
          <span>
            {step.label}
            <span className="sr-only"> ({stepText[step.state]})</span>
          </span>
        </li>
      ))}
    </ol>
  );
}
