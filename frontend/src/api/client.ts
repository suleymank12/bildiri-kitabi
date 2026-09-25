import createClient from 'openapi-fetch';
import { ApiError, parseRetryAfter, toProblem } from './errors';
import type { paths } from './schema';

/**
 * Typed client for the backend. Requests go to the same origin (`/api/...`): the Vite dev server and, later,
 * nginx forward them to the API. The origin is spelled out because `fetch` outside a browser needs absolute URLs.
 * `fetch` is looked up on every call rather than captured once, so request mocks installed later take effect.
 */
export const api = createClient<paths>({
  baseUrl: window.location.origin,
  fetch: (request) => globalThis.fetch(request),
});

interface FetchResult<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

/** Awaits an openapi-fetch call and returns its data, or throws an {@link ApiError}. */
export async function unwrap<T>(call: Promise<FetchResult<T>>): Promise<T> {
  let result: FetchResult<T>;
  try {
    result = await call;
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') {
      throw cause;
    }

    throw new ApiError(0);
  }

  const { data, error, response } = result;
  if (!response.ok) {
    throw new ApiError(
      response.status,
      toProblem(error),
      parseRetryAfter(response.headers.get('Retry-After')),
    );
  }

  return data as T;
}
