import { Fragment, type ReactNode } from 'react';

const breakAfter = new Set(['_', '-', '.']);

/**
 * Splits a file name into the pieces between which a line may break: after `_`, `-` and `.`, never inside a
 * word and never inside or right before the extension ("01_Akilli_Sulama.docx" → "01_", "Akilli_", "Sulama.docx").
 */
export function fileNameSegments(name: string): string[] {
  const extensionStart = name.lastIndexOf('.');
  const base = extensionStart > 0 ? name.slice(0, extensionStart) : name;
  const extension = extensionStart > 0 ? name.slice(extensionStart) : '';

  const segments: string[] = [];
  let current = '';
  for (const char of base) {
    current += char;
    // A separator alone (a leading dot, a doubled underscore) is not worth a break of its own.
    if (breakAfter.has(char) && current.length > 1) {
      segments.push(current);
      current = '';
    }
  }

  current += extension;
  if (current.length > 0 || segments.length === 0) {
    segments.push(current);
  }

  return segments;
}

/** The file name with `<wbr>` break opportunities from {@link fileNameSegments}. */
export function BreakableFileName({ name }: { name: string }): ReactNode {
  const segments = fileNameSegments(name);
  return segments.map((segment, index) => (
    <Fragment key={index}>
      {index > 0 && <wbr />}
      {segment}
    </Fragment>
  ));
}
