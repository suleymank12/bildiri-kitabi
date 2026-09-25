import { ArrowCounterClockwiseIcon, ListNumbersIcon } from '@phosphor-icons/react';
import type { BookDetail } from '../../../api/types';
import { Alert, Button, Card } from '../../../components/ui';
import { StageList } from './GenerationProgress';
import { stageSteps } from './stages';

export interface FailurePanelProps {
  book: BookDetail;
  retrying: boolean;
  onRetry: () => void;
  onEditOrder: () => void;
}

/** A failed generation: where it stopped, the server's message and code, and the two ways forward. */
export function FailurePanel({ book, retrying, onRetry, onEditOrder }: FailurePanelProps) {
  return (
    <div className="flex flex-col gap-6">
      <Alert
        tone="danger"
        title="Kitap oluşturulamadı"
        action={
          <div className="flex flex-col gap-2 sm:flex-row">
            <Button
              variant="primary"
              loading={retrying}
              icon={<ArrowCounterClockwiseIcon size={16} aria-hidden="true" />}
              onClick={onRetry}
            >
              Tekrar dene
            </Button>
            <Button
              variant="secondary"
              icon={<ListNumbersIcon size={16} aria-hidden="true" />}
              disabled={retrying}
              onClick={onEditOrder}
            >
              Sırayı düzenle
            </Button>
          </div>
        }
      >
        <p className="text-ink">{book.error?.message ?? 'Beklenmeyen bir hata oluştu.'}</p>
        {book.error?.code && <p className="mt-1 text-xs text-ink-muted">Hata kodu: {book.error.code}</p>}
      </Alert>
      <Card className="p-5 sm:p-6">
        <h2 className="mb-4 text-xl">Hazırlık adımları</h2>
        <StageList steps={stageSteps(book)} />
      </Card>
    </div>
  );
}
