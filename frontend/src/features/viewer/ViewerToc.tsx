import type { Paper } from '../../api/types';

export interface ViewerTocProps {
  papers: readonly Paper[];
  /** Id of the paper shown on screen, highlighted in the list. */
  activePaperId: string | undefined;
  onSelect: (page: number) => void;
}

/** The papers with their start pages; choosing one opens its first page. */
export function ViewerToc({ papers, activePaperId, onSelect }: ViewerTocProps) {
  return (
    <ol aria-label="İçindekiler" className="flex flex-col">
      {papers.map((paper) => {
        const active = paper.id === activePaperId;
        const start = paper.startPage;
        return (
          <li key={paper.id}>
            <button
              type="button"
              disabled={start == null}
              aria-current={active ? 'true' : undefined}
              onClick={() => {
                if (start != null) {
                  onSelect(start);
                }
              }}
              className={
                'relative grid min-h-11 w-full grid-cols-[2.5ch_minmax(0,1fr)_auto] items-baseline gap-x-2 rounded-(--radius-sm) px-3 py-2 text-left transition-colors duration-150 ' +
                (active ? 'bg-accent-soft text-ink' : 'text-ink hover:bg-surface-muted')
              }
            >
              <span className={`numeric text-sm ${active ? 'text-accent' : 'text-ink-muted'}`}>
                {paper.order}.
              </span>
              <span className="font-serif text-sm leading-snug">{paper.title}</span>
              <span className="numeric text-sm text-ink-muted">
                <span className="sr-only">sayfa </span>
                {start ?? '–'}
              </span>
            </button>
          </li>
        );
      })}
    </ol>
  );
}

/** The paper that owns one of the visible pages (the right-hand page wins in a spread). */
export function paperOnPages(papers: readonly Paper[], pages: readonly number[]): Paper | undefined {
  for (const page of [...pages].sort((a, b) => b - a)) {
    const paper = papers.find(
      (p) => p.startPage != null && p.endPage != null && page >= p.startPage && page <= p.endPage,
    );
    if (paper) {
      return paper;
    }
  }

  return undefined;
}
