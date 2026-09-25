import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type Announcements,
  type DragEndEvent,
} from '@dnd-kit/core';
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ArrowDownIcon, ArrowUpIcon, DotsSixVerticalIcon } from '@phosphor-icons/react';
import type { Paper } from '../../api/types';
import { Badge, Button } from '../../components/ui';
import { BreakableFileName } from '../../lib/fileName';
import { formatBytes } from '../../lib/format';
import { titleSourceLabel, titleSourceTone } from '../../lib/status';

export interface PaperOrderListProps {
  papers: readonly Paper[];
  /** Called with the full new order of paper ids. */
  onReorder: (paperIds: string[]) => void;
  locked: boolean;
}

function position(papers: readonly Paper[], id: string | number): number {
  return papers.findIndex((paper) => paper.id === id) + 1;
}

export function PaperOrderList({ papers, onReorder, locked }: PaperOrderListProps) {
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const nameOf = (id: string | number) => papers.find((paper) => paper.id === id)?.fileName ?? '';
  const announcements: Announcements = {
    onDragStart: ({ active }) => `${nameOf(active.id)} tutuldu. Sırası ${position(papers, active.id)}.`,
    onDragOver: ({ active, over }) =>
      over
        ? `${nameOf(active.id)}, ${position(papers, over.id)}. sıranın üzerinde.`
        : `${nameOf(active.id)} listenin dışında.`,
    onDragEnd: ({ active, over }) =>
      over
        ? `${nameOf(active.id)} ${position(papers, over.id)}. sıraya bırakıldı.`
        : `${nameOf(active.id)} bırakıldı.`,
    onDragCancel: ({ active }) => `Taşıma iptal edildi. ${nameOf(active.id)} yerinde kaldı.`,
  };

  function move(from: number, to: number) {
    if (to < 0 || to >= papers.length || from === to) {
      return;
    }

    onReorder(arrayMove([...papers], from, to).map((paper) => paper.id));
  }

  function handleDragEnd({ active, over }: DragEndEvent) {
    if (!over || active.id === over.id) {
      return;
    }

    move(position(papers, active.id) - 1, position(papers, over.id) - 1);
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCenter}
      onDragEnd={handleDragEnd}
      accessibility={{
        announcements,
        screenReaderInstructions: {
          draggable:
            'Taşımak için boşluk veya Enter tuşuna basın, ok tuşlarıyla yerini değiştirin, bırakmak için yeniden boşluk veya Enter, iptal için Esc tuşuna basın.',
        },
      }}
    >
      <SortableContext
        items={papers.map((paper) => paper.id)}
        strategy={verticalListSortingStrategy}
        disabled={locked}
      >
        <div
          aria-hidden="true"
          className="hidden grid-cols-[2.75rem_2.5rem_minmax(0,1fr)_6rem_auto] gap-4 border-b border-line px-3 pb-2 text-xs font-medium tracking-wide text-ink-muted uppercase md:grid"
        >
          <span />
          <span>Sıra</span>
          <span>Dosya ve başlık</span>
          <span className="text-right">Boyut</span>
          <span />
        </div>
        <ol aria-label="Bildiri sırası" className="flex flex-col gap-2 md:gap-0">
          {papers.map((paper, index) => (
            <SortablePaper
              key={paper.id}
              paper={paper}
              index={index}
              count={papers.length}
              locked={locked}
              onMove={move}
            />
          ))}
        </ol>
      </SortableContext>
    </DndContext>
  );
}

interface SortablePaperProps {
  paper: Paper;
  index: number;
  count: number;
  locked: boolean;
  onMove: (from: number, to: number) => void;
}

function SortablePaper({ paper, index, count, locked, onMove }: SortablePaperProps) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } =
    useSortable({
      id: paper.id,
      disabled: locked,
    });

  // Phones: handle, number and file name on the first line; title, source + size and the move buttons below at
  // full card width. Wide screens: one table row.
  return (
    <li
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      className={
        'grid grid-cols-[2.75rem_2rem_minmax(0,1fr)] items-center gap-x-2 gap-y-2 rounded-(--radius) border border-line bg-surface px-3 py-3 ' +
        "[grid-template-areas:'handle_order_name'_'title_title_title'_'meta_meta_meta'_'actions_actions_actions'] " +
        'md:grid-cols-[2.75rem_2.5rem_minmax(0,1fr)_6rem_auto] md:gap-x-4 md:gap-y-1 md:rounded-none md:border-0 md:border-b ' +
        "md:[grid-template-areas:'handle_order_name_size_actions'_'handle_order_title_size_actions'_'handle_order_meta_size_actions'] " +
        (isDragging ? 'relative z-10 shadow-(--shadow-raised) md:rounded-(--radius) md:border' : '')
      }
    >
      <button
        ref={setActivatorNodeRef}
        type="button"
        disabled={locked}
        className="flex size-11 cursor-grab touch-none items-center justify-center rounded-(--radius) text-ink-subtle [grid-area:handle] hover:bg-surface-muted hover:text-ink disabled:cursor-not-allowed disabled:opacity-50"
        aria-label={`${paper.fileName} dosyasını sürükleyerek taşı`}
        {...attributes}
        {...listeners}
      >
        <DotsSixVerticalIcon size={20} aria-hidden="true" />
      </button>
      <span className="numeric text-sm text-ink-muted [grid-area:order]">
        {String(index + 1).padStart(2, '0')}
      </span>
      <span className="min-w-0 font-medium wrap-break-word text-ink [grid-area:name] md:self-end">
        <BreakableFileName name={paper.fileName} />
      </span>
      <span className="font-serif text-[0.9375rem] leading-snug text-ink-muted [grid-area:title]">
        {paper.title}
      </span>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 [grid-area:meta] md:self-start">
        <Badge tone={titleSourceTone(paper.titleSource)}>{titleSourceLabel(paper.titleSource)}</Badge>
        <span className="numeric text-sm text-ink-muted md:hidden">
          Boyut: {formatBytes(paper.sizeBytes)}
        </span>
      </div>
      <span className="numeric hidden text-right text-sm text-ink-muted [grid-area:size] md:block">
        {formatBytes(paper.sizeBytes)}
      </span>
      <div className="flex justify-end gap-1 border-t border-line pt-2 [grid-area:actions] md:border-0 md:pt-0">
        <Button
          variant="ghost"
          size="sm"
          icon={<ArrowUpIcon size={16} aria-hidden="true" />}
          disabled={locked || index === 0}
          aria-label={`${paper.fileName} dosyasını yukarı taşı`}
          onClick={() => {
            onMove(index, index - 1);
          }}
        >
          <span className="md:sr-only">Yukarı</span>
        </Button>
        <Button
          variant="ghost"
          size="sm"
          icon={<ArrowDownIcon size={16} aria-hidden="true" />}
          disabled={locked || index === count - 1}
          aria-label={`${paper.fileName} dosyasını aşağı taşı`}
          onClick={() => {
            onMove(index, index + 1);
          }}
        >
          <span className="md:sr-only">Aşağı</span>
        </Button>
      </div>
    </li>
  );
}
