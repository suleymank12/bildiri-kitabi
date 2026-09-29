import { render, screen } from '@testing-library/react';
import { MemoryRouter, useLocation, useNavigationType } from 'react-router';
import { usePageOutOfRange } from './Pagination';

type List = Parameters<typeof usePageOutOfRange>[1];

function Probe({ page, list }: { page: number; list: List }) {
  const waiting = usePageOutOfRange(page, list);
  const location = useLocation();
  return (
    <p data-testid="probe">
      {`${location.search || '(yok)'} ${useNavigationType()} ${waiting ? 'bekliyor' : 'hazır'}`}
    </p>
  );
}

function renderProbe(route: string, page: number, list: List) {
  render(
    <MemoryRouter initialEntries={[route]}>
      <Probe page={page} list={list} />
    </MemoryRouter>,
  );
  return screen.getByTestId('probe');
}

const empty = (page: number, totalCount: number) => ({ items: [], page, totalCount });

describe('usePageOutOfRange', () => {
  it('replaces a page past the end with the last page, without a new history entry', () => {
    expect(
      renderProbe('/?sayfa=99', 99, { data: empty(99, 45), isPlaceholderData: false }),
    ).toHaveTextContent('?sayfa=3 REPLACE bekliyor');
  });

  it('goes to page 1 (no parameter) when nothing is left', () => {
    expect(renderProbe('/?sayfa=2', 2, { data: empty(2, 0), isPlaceholderData: false })).toHaveTextContent(
      '(yok) REPLACE bekliyor',
    );
  });

  it('leaves an empty page 1 alone: the list shows its empty state', () => {
    expect(renderProbe('/', 1, { data: empty(1, 0), isPlaceholderData: false })).toHaveTextContent(
      '(yok) POP hazır',
    );
  });

  it('does not act on the previous page shown while this one loads', () => {
    expect(renderProbe('/?sayfa=2', 2, { data: empty(99, 21), isPlaceholderData: true })).toHaveTextContent(
      '?sayfa=2 POP bekliyor',
    );
  });

  it('does not act on an answer for another page', () => {
    expect(renderProbe('/?sayfa=2', 2, { data: empty(3, 21), isPlaceholderData: false })).toHaveTextContent(
      '?sayfa=2 POP bekliyor',
    );
  });

  it('keeps showing the previous page while this one loads when it has books', () => {
    const data = { items: [{}], page: 1, totalCount: 21 };
    expect(renderProbe('/?sayfa=2', 2, { data, isPlaceholderData: true })).toHaveTextContent(
      '?sayfa=2 POP hazır',
    );
  });
});
