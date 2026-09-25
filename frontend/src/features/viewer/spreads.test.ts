import { buildSpreads, sideOfPage, spreadIndexOfPage } from './spreads';

describe('buildSpreads', () => {
  it('puts the cover alone, then facing pages, and the last even page alone for 22 pages', () => {
    const spreads = buildSpreads(22, 'double');

    expect(spreads[0]).toEqual([1]);
    expect(spreads[1]).toEqual([2, 3]);
    expect(spreads[10]).toEqual([20, 21]);
    expect(spreads.at(-1)).toEqual([22]);
    expect(spreads).toHaveLength(12);
    expect(spreads.flat()).toEqual(Array.from({ length: 22 }, (_, i) => i + 1));
  });

  it('ends on a full pair when the page count is odd', () => {
    expect(buildSpreads(21, 'double').at(-1)).toEqual([20, 21]);
    expect(buildSpreads(3, 'double')).toEqual([[1], [2, 3]]);
  });

  it('handles tiny and empty books', () => {
    expect(buildSpreads(1, 'double')).toEqual([[1]]);
    expect(buildSpreads(2, 'double')).toEqual([[1], [2]]);
    expect(buildSpreads(0, 'double')).toEqual([]);
  });

  it('shows one page per spread in single mode', () => {
    expect(buildSpreads(3, 'single')).toEqual([[1], [2], [3]]);
  });
});

describe('spreadIndexOfPage', () => {
  it.each([
    [1, 0],
    [2, 1],
    [3, 1],
    [5, 2],
    [21, 10],
    [22, 11],
  ])('double: page %d is on spread %d', (page, index) => {
    expect(spreadIndexOfPage(page, 22, 'double')).toBe(index);
  });

  it('single: the page index', () => {
    expect(spreadIndexOfPage(7, 22, 'single')).toBe(6);
  });

  it('clamps pages outside the book', () => {
    expect(spreadIndexOfPage(0, 22, 'double')).toBe(0);
    expect(spreadIndexOfPage(99, 22, 'double')).toBe(11);
    expect(spreadIndexOfPage(3, 0, 'double')).toBe(0);
  });
});

describe('sideOfPage', () => {
  it('places papers, which start on odd pages, on the right', () => {
    expect([1, 2, 3, 21, 22].map(sideOfPage)).toEqual(['right', 'left', 'right', 'right', 'left']);
  });
});
