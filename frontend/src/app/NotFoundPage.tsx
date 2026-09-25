import { Link } from 'react-router';
import { EmptyState, buttonClasses } from '../components/ui';
import { usePageTitle } from './usePageTitle';

export function NotFoundPage() {
  usePageTitle('Sayfa bulunamadı');
  return (
    <EmptyState
      title="Sayfa bulunamadı"
      action={
        <Link to="/" className={buttonClasses('primary')}>
          Yeni kitap
        </Link>
      }
    >
      Aradığınız sayfa taşınmış veya hiç var olmamış olabilir.
    </EmptyState>
  );
}
