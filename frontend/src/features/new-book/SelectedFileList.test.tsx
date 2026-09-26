import { render, screen, within } from '@testing-library/react';
import { FILE_TABLE_COLUMNS, NUMERIC_COLUMN } from '../../lib/tableColumns';
import { docx } from '../../test/fixtures';
import { SelectedFileList } from './SelectedFileList';

/** The md: text alignment classes of an element, e.g. ["md:text-center"]. */
const alignment = (element: Element) =>
  [...element.classList].filter((name) => /^md:text-(left|center|right)$/.test(name));

describe('SelectedFileList', () => {
  it('lays out the heading and the rows with the same columns and the same alignment per column', () => {
    const { container } = render(
      <SelectedFileList
        files={[{ id: 'a', file: docx('01_Bildiri.docx'), checking: false }]}
        serverErrors={new Map()}
        onRemove={() => undefined}
      />,
    );

    const heading = container.querySelector('[aria-hidden="true"]')!;
    const row = within(screen.getByRole('list', { name: 'Seçilen dosyalar' })).getByRole('listitem');
    expect(heading.className).toContain(FILE_TABLE_COLUMNS);
    expect(row.className).toContain(FILE_TABLE_COLUMNS);

    const headingCells = [...heading.children];
    const rowCells = [...row.children];
    expect(rowCells).toHaveLength(headingCells.length);
    headingCells.forEach((cell, index) => {
      expect(alignment(rowCells[index]!)).toEqual(alignment(cell));
    });
    // Size is a short number of a fixed shape: centred, heading included.
    expect(headingCells[2]).toHaveTextContent('Boyut');
    expect(headingCells[2]).toHaveClass(NUMERIC_COLUMN);
    expect(rowCells[2]).toHaveClass(NUMERIC_COLUMN, 'numeric');
  });
});
