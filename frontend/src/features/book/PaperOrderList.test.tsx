import { render, screen, within } from '@testing-library/react';
import type { TitleSource } from '../../api/types';
import { paper } from '../../test/fixtures';
import { PaperOrderList } from './PaperOrderList';

function renderRow(titleSource: TitleSource) {
  render(<PaperOrderList papers={[paper(1, { titleSource })]} onReorder={vi.fn()} locked={false} />);
  return screen.getByRole('listitem');
}

describe('PaperOrderList — title source', () => {
  it('shows no note for a title found by the title style', () => {
    const row = renderRow('TitleStyle');

    expect(within(row).queryByText(/kontrol edin/i)).not.toBeInTheDocument();
    expect(within(row).queryByText('Başlık stilinden')).not.toBeInTheDocument();
  });

  it.each<[TitleSource, string]>([
    ['FirstBoldParagraph', 'Başlık ilk kalın paragraftan alındı, kontrol edin.'],
    ['FileName', 'Başlık bulunamadı, dosya adı kullanıldı. Kontrol edin.'],
  ])('asks to check a title from %s as part of the row, not as an alert', (source, note) => {
    const row = renderRow(source);

    expect(row).toHaveTextContent(note);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
