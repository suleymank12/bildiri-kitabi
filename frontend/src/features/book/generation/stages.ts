import type { BookDetail, GenerationStage } from '../../../api/types';
import { GENERATION_STAGES, QUEUED_LABEL, stageLabel } from '../../../lib/status';

export type StepState = 'done' | 'current' | 'pending' | 'failed';

export interface StageStep {
  key: 'Queued' | GenerationStage;
  label: string;
  state: StepState;
}

type BookState = Pick<BookDetail, 'status' | 'stage'>;

/**
 * The vertical list of the progress view, from the real server stages: waiting in the queue, then every
 * generation stage. Steps before the current one are done, the rest pending; a failed book marks the stage
 * where it stopped.
 */
export function stageSteps(book: BookState): StageStep[] {
  const keys: StageStep['key'][] = ['Queued', ...GENERATION_STAGES];
  const current = currentIndex(book);

  return keys.map((key, index) => {
    let state: StepState;
    if (book.status === 'Completed') {
      state = 'done';
    } else if (index < current) {
      state = 'done';
    } else if (index === current) {
      state = book.status === 'Failed' ? 'failed' : 'current';
    } else {
      state = 'pending';
    }

    return { key, label: key === 'Queued' ? QUEUED_LABEL : stageLabel(key), state };
  });
}

function currentIndex(book: BookState): number {
  switch (book.status) {
    case 'Uploaded':
    case 'Queued':
      return 0;
    case 'Completed':
      return GENERATION_STAGES.length + 1;
    case 'Processing':
    case 'Failed':
      // A job that has been claimed but has not reported yet is reading its documents.
      return book.stage ? GENERATION_STAGES.indexOf(book.stage) + 1 : 1;
  }
}

/** Label of the step that is running now, for announcements. */
export function currentStepLabel(book: BookState): string | undefined {
  return stageSteps(book).find((step) => step.state === 'current')?.label;
}
