import { QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router';
import { createQueryClient } from '../app/App';
import { AnnouncerProvider } from '../app/Announcer';

/** Renders a page inside the app providers at the given URL. */
export function renderPage(
  element: ReactElement,
  { path = '/', route = '/' }: { path?: string; route?: string } = {},
) {
  const queryClient = createQueryClient();
  queryClient.setDefaultOptions({
    queries: { retry: false, refetchOnWindowFocus: false },
    mutations: { retry: false },
  });
  // The file input filters by `accept` in browsers; tests pass wrong files on purpose to check the validation.
  const user = userEvent.setup({ applyAccept: false });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <AnnouncerProvider>
        <MemoryRouter initialEntries={[route]}>
          <Routes>
            <Route path={path} element={element} />
            <Route path="*" element={<p>Başka sayfa</p>} />
          </Routes>
        </MemoryRouter>
      </AnnouncerProvider>
    </QueryClientProvider>,
  );
  return { ...result, user, queryClient };
}
