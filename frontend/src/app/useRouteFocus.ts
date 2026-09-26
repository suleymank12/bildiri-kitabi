import { useEffect, useRef } from 'react';
import { NavigationType, useLocation, useNavigationType } from 'react-router';

/** How long to wait for the new page's heading (a book page shows a skeleton until its data arrives). */
const HEADING_WAIT_MS = 5000;

/**
 * After moving to another page (a link or a redirect, not Back/Forward, which keep the browser's own scroll
 * position): scrolls to the top and moves the focus to the new page's `h1`, so keyboard and screen-reader users
 * land on the new page and hear its title. The first page load keeps the browser's default.
 */
export function useRouteFocus(main: React.RefObject<HTMLElement | null>): void {
  const { pathname } = useLocation();
  const navigationType = useNavigationType();
  const first = useRef(true);

  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }

    if (navigationType === NavigationType.Pop) {
      return;
    }

    window.scrollTo({ top: 0, left: 0 });
    const container = main.current;
    if (!container) {
      return;
    }

    const focusHeading = (): boolean => {
      const heading = container.querySelector<HTMLElement>('h1');
      if (!heading) {
        return false;
      }

      if (!heading.hasAttribute('tabindex')) {
        heading.setAttribute('tabindex', '-1');
      }

      heading.focus({ preventScroll: true });
      return true;
    };

    if (focusHeading()) {
      return;
    }

    const observer = new MutationObserver(() => {
      if (focusHeading()) {
        observer.disconnect();
      }
    });
    observer.observe(container, { childList: true, subtree: true });
    const timer = window.setTimeout(() => {
      observer.disconnect();
    }, HEADING_WAIT_MS);
    return () => {
      observer.disconnect();
      window.clearTimeout(timer);
    };
  }, [main, navigationType, pathname]);
}
