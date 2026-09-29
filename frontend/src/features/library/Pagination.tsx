import { CaretLeftIcon, CaretRightIcon } from '@phosphor-icons/react';
import { useEffect } from 'react';
import { useSearchParams } from 'react-router';
import { LIST_PAGE_SIZE } from '../../api/hooks';
import { Button } from '../../components/ui';

/** The current list page from `?sayfa=` (1 when missing or invalid) and a way to move to another one. */
export function usePageParam(): [number, (page: number) => void] {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = Math.max(1, Number.parseInt(searchParams.get('sayfa') ?? '1', 10) || 1);
  return [
    page,
    (target: number) => {
      setSearchParams(target === 1 ? {} : { sayfa: String(target) });
    },
  ];
}

/**
 * Moves a page past the end (its last book was deleted or restored, or `?sayfa=99` was typed) to the last page
 * that exists, or to page 1 when nothing is left, without a new history entry. Only the page's own answer counts:
 * the previous page shown while it loads (placeholder data) is not acted on. True while the list should show its
 * loading view instead: the move is pending, or only an empty stand-in for this page is at hand.
 */
export function usePageOutOfRange(
  page: number,
  list: {
    data?: { items: readonly unknown[]; page: number; totalCount: number };
    isPlaceholderData: boolean;
  },
): boolean {
  const [, setSearchParams] = useSearchParams();
  const { data, isPlaceholderData } = list;
  const own = data !== undefined && !isPlaceholderData && data.page === page;
  const lastPage = data ? Math.max(1, Math.ceil(data.totalCount / LIST_PAGE_SIZE)) : 1;
  const outOfRange = own && page > 1 && data.items.length === 0;

  useEffect(() => {
    if (outOfRange) {
      setSearchParams(lastPage === 1 ? {} : { sayfa: String(lastPage) }, { replace: true });
    }
  }, [outOfRange, lastPage, setSearchParams]);

  return outOfRange || (data !== undefined && !own && data.items.length === 0);
}

/** "Önceki / Sayfa x / y / Sonraki" under a paged list; nothing when everything fits on one page. */
export function Pagination({
  page,
  totalCount,
  onChange,
}: {
  page: number;
  totalCount: number;
  onChange: (page: number) => void;
}) {
  const pageCount = Math.max(1, Math.ceil(totalCount / LIST_PAGE_SIZE));
  if (pageCount <= 1) {
    return null;
  }

  return (
    <nav aria-label="Sayfalar" className="flex items-center justify-between gap-3">
      <Button
        variant="secondary"
        size="sm"
        icon={<CaretLeftIcon size={16} aria-hidden="true" />}
        disabled={page <= 1}
        onClick={() => {
          onChange(page - 1);
        }}
      >
        Önceki
      </Button>
      <span className="text-sm text-ink-muted">
        Sayfa <span className="numeric">{page}</span> / <span className="numeric">{pageCount}</span>
      </span>
      <Button
        variant="secondary"
        size="sm"
        disabled={page >= pageCount}
        onClick={() => {
          onChange(page + 1);
        }}
      >
        Sonraki
        <CaretRightIcon size={16} aria-hidden="true" />
      </Button>
    </nav>
  );
}
