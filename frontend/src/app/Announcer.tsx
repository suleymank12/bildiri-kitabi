import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';

type Announce = (message: string) => void;

const AnnouncerContext = createContext<Announce>(() => undefined);

/**
 * One polite live region for the whole app: status changes (upload finished, book ready, …) are read out without
 * moving the focus.
 */
export function AnnouncerProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState('');
  const [count, setCount] = useState(0);

  const announce = useCallback<Announce>((text) => {
    setMessage(text);
    setCount((value) => value + 1);
  }, []);

  const value = useMemo(() => announce, [announce]);

  return (
    <AnnouncerContext.Provider value={value}>
      {children}
      <div aria-live="polite" aria-atomic="true" className="sr-only">
        {/* The key makes the same message readable twice in a row. */}
        <span key={count}>{message}</span>
      </div>
    </AnnouncerContext.Provider>
  );
}

export function useAnnounce(): Announce {
  return useContext(AnnouncerContext);
}
