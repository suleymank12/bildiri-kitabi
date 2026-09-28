export type ViewMode = 'single' | 'double';

/**
 * Pages grouped as they appear on screen. Single: one page each. Double (a printed book): the cover alone,
 * then facing pages [2, 3], [4, 5], … — even pages on the left, odd on the right — so every paper, which starts
 * on an odd page, opens on a right-hand page. A last even page stands alone on the left.
 */
export function buildSpreads(pageCount: number, mode: ViewMode): number[][] {
  const count = Math.max(0, Math.floor(pageCount));
  if (count === 0) {
    return [];
  }

  if (mode === 'single') {
    return Array.from({ length: count }, (_, i) => [i + 1]);
  }

  const spreads: number[][] = [[1]];
  for (let left = 2; left <= count; left += 2) {
    spreads.push(left + 1 <= count ? [left, left + 1] : [left]);
  }

  return spreads;
}

/** Index of the spread that shows the page (pages outside the book fall on the first or last spread). */
export function spreadIndexOfPage(page: number, pageCount: number, mode: ViewMode): number {
  const spreads = buildSpreads(pageCount, mode);
  if (spreads.length === 0) {
    return 0;
  }

  const index = spreads.findIndex((spread) => spread.includes(page));
  if (index >= 0) {
    return index;
  }

  return page < 1 ? 0 : spreads.length - 1;
}
