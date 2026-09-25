let viewportWidth = 1280;

/** Width that `(min-width: …)` / `(max-width: …)` media queries are evaluated against in tests. */
export function setViewportWidth(width: number): void {
  viewportWidth = width;
}

export function resetViewport(): void {
  viewportWidth = 1280;
}

function matches(query: string): boolean {
  const min = /min-width:\s*(\d+)px/.exec(query);
  const max = /max-width:\s*(\d+)px/.exec(query);
  return (!min || viewportWidth >= Number(min[1])) && (!max || viewportWidth <= Number(max[1]));
}

export function mediaQueryList(query: string): MediaQueryList {
  return {
    media: query,
    matches: matches(query),
    onchange: null,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    addListener: () => undefined,
    removeListener: () => undefined,
    dispatchEvent: () => false,
  };
}
