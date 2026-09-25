import { setupServer } from 'msw/node';

/** Mock API for component tests; each test registers the handlers it needs with `server.use(...)`. */
export const server = setupServer();
