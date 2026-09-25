import { formatBytes, formatDate, formatDuration, possessiveSuffix, withPossessive } from './format';

describe('formatBytes', () => {
  it.each([
    [812, '812 B'],
    [26_010, '25,4 KB'],
    [10 * 1024 * 1024, '10 MB'],
    [131_700, '128,6 KB'],
  ])('%d → %s', (bytes, expected) => {
    expect(formatBytes(bytes)).toBe(expected);
  });
});

describe('formatDate', () => {
  it('uses Turkish month names', () => {
    expect(formatDate(new Date(2026, 8, 29))).toBe('29 Eylül 2026');
  });
});

describe('withPossessive', () => {
  it.each([
    [0, "0'ı"],
    [1, "1'i"],
    [2, "2'si"],
    [3, "3'ü"],
    [4, "4'ü"],
    [5, "5'i"],
    [6, "6'sı"],
    [7, "7'si"],
    [8, "8'i"],
    [9, "9'u"],
    [10, "10'u"],
    [11, "11'i"],
    [12, "12'si"],
    [20, "20'si"],
    [40, "40'ı"],
    [100, "100'ü"],
  ])('%d → %s', (value, expected) => {
    expect(withPossessive(value)).toBe(expected);
    expect(possessiveSuffix(value)).toBe(expected.slice(expected.indexOf("'")));
  });
});

describe('formatDuration', () => {
  it.each([
    [0, '0 sn'],
    [8.7, '8 sn'],
    [59, '59 sn'],
    [60, '1 dk'],
    [65, '1 dk 5 sn'],
    [-3, '0 sn'],
  ])('%d → %s', (seconds, expected) => {
    expect(formatDuration(seconds)).toBe(expected);
  });
});
