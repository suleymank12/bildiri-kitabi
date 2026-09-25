import { defineConfig } from '@playwright/test';

process.env.E2E_BASE_URL ??= 'http://localhost:8080';

/**
 * The end-to-end tests against the Docker setup (`docker compose up --build -d`, then `npm run test:e2e:docker`).
 * The base configuration is loaded after E2E_BASE_URL is set, so it starts no servers of its own.
 */
const { default: baseConfig } = await import('./playwright.config');

export default defineConfig(baseConfig);
