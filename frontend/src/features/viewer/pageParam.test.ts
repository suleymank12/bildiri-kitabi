import { parsePageParam } from './pageParam';

describe('parsePageParam', () => {
  it.each([
    ['5', 5],
    ['1', 1],
    ['22', 22],
    [' 7 ', 7],
    ['007', 7],
  ])('%j → %d', (value, page) => {
    expect(parsePageParam(value, 22)).toBe(page);
  });

  it.each([null, '', 'abc', '0', '-3', '2.5', '23', '1e2', '5a'])('%j falls back to 1', (value) => {
    expect(parsePageParam(value, 22)).toBe(1);
  });

  it('accepts only page 1 before the page count is known', () => {
    expect(parsePageParam('3', 0)).toBe(1);
  });
});
