import { useEffect } from 'react';

export const APP_NAME = 'Bildiri Kitabı';

/** "Yeni kitap — Bildiri Kitabı" */
export function usePageTitle(title: string | undefined): void {
  useEffect(() => {
    document.title = title ? `${title} — ${APP_NAME}` : APP_NAME;
  }, [title]);
}
