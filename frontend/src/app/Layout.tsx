import { BooksIcon } from '@phosphor-icons/react';
import { useRef, type ReactNode } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router';
import { AnnouncerProvider } from './Announcer';
import { paths } from './paths';
import { ErrorBoundary } from './ErrorBoundary';
import { useRouteFocus } from './useRouteFocus';

function NavItem({
  to,
  end,
  icon,
  label,
  short,
}: {
  to: string;
  end?: boolean;
  icon: ReactNode;
  label: string;
  short: string;
}) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        'inline-flex min-h-11 items-center gap-2 rounded-(--radius) px-3 text-sm font-medium transition-colors duration-[130ms] motion-reduce:transition-none ' +
        (isActive
          ? 'bg-accent-soft text-accent'
          : 'text-ink-muted hover:bg-line/60 hover:text-ink active:bg-line')
      }
    >
      <span aria-hidden="true">{icon}</span>
      <span className="sm:hidden">{short}</span>
      <span className="hidden sm:inline">{label}</span>
    </NavLink>
  );
}

export function Layout() {
  const location = useLocation();
  const main = useRef<HTMLElement>(null);
  useRouteFocus(main);
  return (
    <AnnouncerProvider>
      <a
        href="#icerik"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:rounded-(--radius) focus:bg-surface focus:px-3 focus:py-2"
      >
        İçeriğe geç
      </a>
      <div className="group/app flex min-h-dvh flex-col">
        <header className="border-b border-line bg-paper">
          <div className="mx-auto flex max-w-[1100px] group-has-[[data-wide-page]]/app:max-w-[1848px] items-center justify-between gap-4 px-4 py-3 sm:px-6">
            <Link to="/" className="flex flex-col leading-tight">
              <span className="font-serif text-xl font-semibold whitespace-nowrap text-ink">
                Bildiri Kitabı
              </span>
              <span className="text-xs text-ink-muted">Bildirilerden e-kitap</span>
            </Link>
            <nav aria-label="Ana menü" className="flex items-center gap-1">
              <NavItem
                to={paths.library}
                end
                icon={<BooksIcon size={20} />}
                label="Kitaplarım"
                short="Kitaplar"
              />
            </nav>
          </div>
        </header>
        <main
          ref={main}
          id="icerik"
          className="mx-auto w-full max-w-[1100px] group-has-[[data-wide-page]]/app:max-w-[1848px] flex-1 px-4 py-8 sm:px-6 sm:py-10"
        >
          {/* Keyed by path: moving to another page clears a caught error. */}
          <ErrorBoundary key={location.pathname}>
            <Outlet />
          </ErrorBoundary>
        </main>
      </div>
    </AnnouncerProvider>
  );
}
