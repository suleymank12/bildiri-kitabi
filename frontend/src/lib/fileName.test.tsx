import { render } from '@testing-library/react';
import { BreakableFileName, fileNameSegments } from './fileName';

describe('fileNameSegments', () => {
  it.each([
    ['01_Akilli_Sulama.docx', ['01_', 'Akilli_', 'Sulama.docx']],
    ['kongre-2026-bildiri.docx', ['kongre-', '2026-', 'bildiri.docx']],
    ['v1.2.son.docx', ['v1.', '2.', 'son.docx']],
    ['bildiri.docx', ['bildiri.docx']],
    ['uzantısız_dosya', ['uzantısız_', 'dosya']],
    ['sonu_.docx', ['sonu_', '.docx']],
    ['.gizli', ['.gizli']],
    ['', ['']],
  ])('%s', (name, segments) => {
    expect(fileNameSegments(name)).toEqual(segments);
    expect(fileNameSegments(name).join('')).toBe(name);
  });

  it('never breaks the extension off or apart', () => {
    for (const name of ['a_b.docx', 'çok-uzun_bir_dosya_adı.docx']) {
      expect(fileNameSegments(name).at(-1)).toMatch(/[^._-]+\.docx$/);
    }
  });
});

describe('BreakableFileName', () => {
  it('renders the full text with <wbr> between the segments', () => {
    const { container } = render(
      <span>
        <BreakableFileName name="01_Akilli_Sulama.docx" />
      </span>,
    );

    expect(container.textContent).toBe('01_Akilli_Sulama.docx');
    expect(container.querySelectorAll('wbr')).toHaveLength(2);
  });
});
