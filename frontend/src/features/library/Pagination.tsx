import { CaretLeftIcon, CaretRightIcon } from '@phosphor-icons/react';
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
