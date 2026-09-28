import { screen } from '@testing-library/react';
import { renderPage } from '../test/render';
import { NotFoundPage } from './NotFoundPage';

describe('NotFoundPage', () => {
  it('says the page does not exist and links back into the app', () => {
    renderPage(<NotFoundPage />, { path: '*', route: '/olmayan-sayfa' });

    expect(screen.getByRole('heading', { name: 'Sayfa bulunamadı' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Kitaplarım' })).toHaveAttribute('href', '/');
    expect(document.title).toBe('Sayfa bulunamadı — Bildiri Kitabı');
  });
});
