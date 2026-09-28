import { QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router';
import { setViewportWidth } from '../test/media';
import { Layout } from './Layout';
import { createQueryClient } from './queryClient';

function renderLayout(route = '/') {
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <MemoryRouter initialEntries={[route]}>
        <Routes>
          <Route element={<Layout />}>
            <Route path="*" element={<h1>Sayfa</h1>} />
          </Route>
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const menu = () => screen.getByRole('navigation', { name: 'Ana menü' });

describe('Layout', () => {
  it('shows the menu as icons with names and tooltips below sm, so it fits a 320 px screen', async () => {
    setViewportWidth(320);
    const user = userEvent.setup();
    renderLayout('/silinenler');

    const library = within(menu()).getByRole('link', { name: 'Kitaplarım' });
    const deleted = within(menu()).getByRole('link', { name: 'Silinenler' });
    expect(library).toHaveAttribute('href', '/');
    expect(library).toHaveClass('min-h-11', 'min-w-11');
    // The written label is hidden below sm; the icon alone shows.
    expect(within(library).getByText('Kitaplarım')).toHaveClass('hidden', 'sm:inline');
    // The current page keeps its highlight.
    expect(deleted).toHaveAttribute('aria-current', 'page');
    expect(deleted).toHaveClass('bg-accent-soft');

    await user.hover(library);
    expect(screen.getByRole('tooltip')).toHaveTextContent('Kitaplarım');
  });

  it('writes the labels out from sm on, without tooltips', async () => {
    setViewportWidth(1280);
    const user = userEvent.setup();
    renderLayout('/');

    const library = within(menu()).getByRole('link', { name: 'Kitaplarım' });
    expect(library).toHaveAttribute('aria-current', 'page');
    await user.hover(library);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });
});
