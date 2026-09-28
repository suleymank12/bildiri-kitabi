import { useSyncExternalStore } from 'react';

/** True while the media query matches, updated live (for example when the window is resized). */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query);
      list.addEventListener('change', onChange);
      return () => {
        list.removeEventListener('change', onChange);
      };
    },
    () => window.matchMedia(query).matches,
    () => false,
  );
}

export const DESKTOP_QUERY = '(min-width: 1024px)';

/** Tailwind's `sm` breakpoint. */
export const SM_QUERY = '(min-width: 640px)';
