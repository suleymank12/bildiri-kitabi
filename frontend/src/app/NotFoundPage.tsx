import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { EmptyState, buttonClasses } from '../components/ui';
import { paths } from './paths';
import { usePageTitle } from './usePageTitle';

/** "Nothing here" with the two ways back into the app; shared by unknown URLs and unknown book ids. */
export function NotFoundState({ title, children }: { title: string; children: ReactNode }) {
  return (
    <EmptyState
      headingLevel="h1"
      title={title}
      action={
        <div className="flex flex-col gap-2 sm:flex-row">
          <Link to={paths.newBook} className={buttonClasses('primary')}>
            Yeni kitap
          </Link>
          <Link to={paths.library} className={buttonClasses('secondary')}>
            Kitaplarım
          </Link>
        </div>
      }
    >
      {children}
    </EmptyState>
  );
}

export function NotFoundPage() {
  usePageTitle('Sayfa bulunamadı');
  return (
    <NotFoundState title="Sayfa bulunamadı">
      Aradığınız sayfa taşınmış veya hiç var olmamış olabilir.
    </NotFoundState>
  );
}
