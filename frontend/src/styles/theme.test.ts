import { readFileSync } from 'node:fs';
import { join } from 'node:path';

// Read from disk: the test runner does not process CSS, so importing it would give an empty module.
const themeCss = readFileSync(join(import.meta.dirname, 'theme.css'), 'utf8');

function tokens(): Map<string, string> {
  const map = new Map<string, string>();
  for (const match of themeCss.matchAll(/--color-([a-z-]+):\s*(#[0-9a-f]{6})/gi)) {
    const [, name, value] = match;
    if (name && value) {
      map.set(name, value);
    }
  }

  return map;
}

function luminance(hex: string): number {
  const channels = [1, 3, 5].map((i) => Number.parseInt(hex.slice(i, i + 2), 16) / 255);
  const [r = 0, g = 0, b = 0] = channels.map((c) =>
    c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4,
  );
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (light + 0.05) / (dark + 0.05);
}

const colors = tokens();
const color = (name: string): string => {
  const value = colors.get(name);
  if (!value) {
    throw new Error(`Token --color-${name} is missing from theme.css`);
  }

  return value;
};

// Text (and the text on buttons, badges and alerts) must reach WCAG AA for normal text: 4.5:1.
const textPairs: [string, string][] = [
  ['ink', 'paper'],
  ['ink', 'surface'],
  ['ink', 'surface-muted'],
  ['ink', 'accent-soft'],
  ['ink', 'danger-soft'],
  ['ink-muted', 'paper'],
  ['ink-muted', 'surface'],
  ['ink-muted', 'surface-muted'],
  ['ink-muted', 'accent-soft'],
  ['ink-muted', 'success-soft'],
  ['ink-muted', 'warning-soft'],
  ['ink-muted', 'danger-soft'],
  ['accent', 'paper'],
  ['accent', 'surface'],
  ['accent', 'accent-soft'],
  ['surface', 'accent'],
  ['surface', 'accent-hover'],
  ['surface', 'accent-active'],
  ['surface', 'danger'],
  ['surface', 'danger-hover'],
  ['success', 'surface'],
  ['success', 'success-soft'],
  ['warning', 'surface'],
  ['warning', 'warning-soft'],
  ['danger', 'surface'],
  ['danger', 'danger-soft'],
];

// Borders of form controls, icons and the focus ring are UI components: 3:1 (WCAG 1.4.11).
const uiPairs: [string, string][] = [
  ['ink-subtle', 'surface'],
  ['ink-subtle', 'surface-muted'],
  ['accent', 'paper'],
  ['accent', 'surface'],
];

describe('theme tokens', () => {
  it('defines every colour of the palette', () => {
    expect([...colors.keys()]).toEqual(
      expect.arrayContaining([
        'paper',
        'surface',
        'surface-muted',
        'ink',
        'ink-muted',
        'ink-subtle',
        'line',
        'line-strong',
        'accent',
        'accent-hover',
        'accent-active',
        'accent-soft',
        'success',
        'success-soft',
        'warning',
        'warning-soft',
        'danger',
        'danger-hover',
        'danger-soft',
      ]),
    );
    expect(color('accent')).toBe('#0e5a5e');
  });

  it.each(textPairs)('%s on %s reaches 4.5:1 for text', (foreground, background) => {
    expect(contrast(color(foreground), color(background))).toBeGreaterThanOrEqual(4.5);
  });

  it.each(uiPairs)('%s on %s reaches 3:1 for UI components', (foreground, background) => {
    expect(contrast(color(foreground), color(background))).toBeGreaterThanOrEqual(3);
  });

  it('computes the reference contrast of black on white', () => {
    expect(contrast('#000000', '#ffffff')).toBeCloseTo(21, 5);
  });
});
