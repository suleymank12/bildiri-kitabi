import { ArrowDownIcon, ArrowUpIcon } from '@phosphor-icons/react';
import { useLayoutEffect, useRef } from 'react';
import type { Paper } from '../../api/types';
import { Badge, Button } from '../../components/ui';
import { BreakableFileName } from '../../lib/fileName';
import { formatBytes } from '../../lib/format';
import { titleSourceLabel, titleSourceTone } from '../../lib/status';

export interface PaperMove {
  paper: Paper;
  /** 1-based position after the move. */
  position: number;
}

export interface PaperOrderListProps {
  papers: readonly Paper[];
  /** Called with the full new order of paper ids and the paper that moved. */
  onReorder: (paperIds: string[], move: PaperMove) => void;
  locked: boolean;
}

type Direction = 'up' | 'down';

// One column template for the header and every row, so each heading sits over its values.
const columns = 'md:grid-cols-[2.5rem_minmax(0,1fr)_6rem_6rem]';

/**
 * The papers in book order. The order changes only with each row's Yukarı / Aşağı buttons; after a move the focus
 * stays on the same paper's button (or its other button once the moved paper reaches the top or the bottom).
 */
export function PaperOrderList({ papers, onReorder, locked }: PaperOrderListProps) {
  const buttons = useRef(new Map<string, HTMLButtonElement>());
  // The button that was pressed; focused again once the list shows the new order.
  const pendingFocus = useRef<{ paperId: string; direction: Direction }>(undefined);

  useLayoutEffect(() => {
    if (!pendingFocus.current) {
      return;
    }

    const { paperId, direction } = pendingFocus.current;
    pendingFocus.current = undefined;
    const same = buttons.current.get(`${paperId}:${direction}`);
    const other = buttons.current.get(`${paperId}:${direction === 'up' ? 'down' : 'up'}`);
    (same && !same.disabled ? same : other)?.focus();
  }, [papers]);

  function move(index: number, direction: Direction) {
    const target = direction === 'up' ? index - 1 : index + 1;
    const paper = papers[index];
    if (!paper || target < 0 || target >= papers.length) {
      return;
    }

    const ids = papers.map((p) => p.id);
    ids.splice(index, 1);
    ids.splice(target, 0, paper.id);
    pendingFocus.current = { paperId: paper.id, direction };
    onReorder(ids, { paper, position: target + 1 });
  }

  const register = (key: string) => (element: HTMLButtonElement | null) => {
    if (element) {
      buttons.current.set(key, element);
    } else {
      buttons.current.delete(key);
    }
  };

  return (
    <div className="flex flex-col">
      <div
        aria-hidden="true"
        className={`hidden gap-4 border-b border-line px-3 pb-2 text-xs font-medium tracking-wide text-ink-muted uppercase md:grid ${columns}`}
      >
        <span>Sıra</span>
        <span>Dosya ve başlık</span>
        <span className="text-right">Boyut</span>
        <span className="sr-only">Sıra değiştir</span>
      </div>
      <ol aria-label="Bildiri sırası" className="flex flex-col gap-2 md:gap-0">
        {papers.map((paper, index) => (
          // Phones: number and file name, then title, source and size, then the move buttons at full width.
          <li
            key={paper.id}
            className={
              'grid grid-cols-[2rem_minmax(0,1fr)] items-start gap-x-2 gap-y-2 rounded-(--radius) border border-line bg-surface px-3 py-3 ' +
              `md:items-center md:gap-x-4 md:rounded-none md:border-0 md:border-b ${columns}`
            }
          >
            <span className="numeric pt-0.5 text-sm text-ink-muted md:pt-0">
              {String(index + 1).padStart(2, '0')}
            </span>
            <div className="flex min-w-0 flex-col gap-1">
              <span className="font-medium wrap-break-word text-ink">
                <BreakableFileName name={paper.fileName} />
              </span>
              <span className="font-serif text-[0.9375rem] leading-snug text-ink-muted">{paper.title}</span>
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                <Badge tone={titleSourceTone(paper.titleSource)}>{titleSourceLabel(paper.titleSource)}</Badge>
                <span className="numeric text-sm text-ink-muted md:hidden">
                  Boyut: {formatBytes(paper.sizeBytes)}
                </span>
              </div>
            </div>
            <span className="numeric hidden text-right text-sm text-ink-muted md:block">
              {formatBytes(paper.sizeBytes)}
            </span>
            <div className="col-span-2 flex justify-end gap-1 border-t border-line pt-2 md:col-span-1 md:border-0 md:pt-0">
              <Button
                ref={register(`${paper.id}:up`)}
                variant="ghost"
                size="sm"
                className="min-w-11 md:px-0"
                icon={<ArrowUpIcon size={16} aria-hidden="true" />}
                disabled={locked || index === 0}
                aria-label={`${paper.fileName} dosyasını yukarı taşı`}
                title="Yukarı taşı"
                onClick={() => {
                  move(index, 'up');
                }}
              >
                <span className="md:sr-only">Yukarı</span>
              </Button>
              <Button
                ref={register(`${paper.id}:down`)}
                variant="ghost"
                size="sm"
                className="min-w-11 md:px-0"
                icon={<ArrowDownIcon size={16} aria-hidden="true" />}
                disabled={locked || index === papers.length - 1}
                aria-label={`${paper.fileName} dosyasını aşağı taşı`}
                title="Aşağı taşı"
                onClick={() => {
                  move(index, 'down');
                }}
              >
                <span className="md:sr-only">Aşağı</span>
              </Button>
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}
