export const PAGE_PARAM = 'sayfa';

/**
 * The page from `?sayfa=`: a whole number between 1 and the page count. Anything else (missing, text,
 * fractions, zero, negative, past the end) opens the first page.
 */
export function parsePageParam(value: string | null, pageCount: number): number {
  if (value === null || !/^\d+$/.test(value.trim())) {
    return 1;
  }

  const page = Number.parseInt(value.trim(), 10);
  return page >= 1 && page <= Math.max(1, pageCount) ? page : 1;
}
