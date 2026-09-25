import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ErrorBoundary } from './ErrorBoundary';

function Broken(): never {
  throw new Error('bozuk bileşen');
}

describe('ErrorBoundary', () => {
  it('shows a plain message and reloads the page on request', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const reload = vi.fn();
    vi.stubGlobal('location', { reload });

    render(
      <ErrorBoundary>
        <Broken />
      </ErrorBoundary>,
    );

    expect(screen.getByRole('heading', { name: 'Bir şeyler ters gitti' })).toBeInTheDocument();
    expect(screen.queryByText('bozuk bileşen')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Sayfayı yenile' }));
    expect(reload).toHaveBeenCalledOnce();
    vi.unstubAllGlobals();
  });

  it('renders its children when nothing fails', () => {
    render(
      <ErrorBoundary>
        <p>İçerik</p>
      </ErrorBoundary>,
    );

    expect(screen.getByText('İçerik')).toBeInTheDocument();
  });
});
