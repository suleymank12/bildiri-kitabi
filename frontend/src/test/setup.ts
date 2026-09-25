import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { mediaQueryList, resetViewport } from './media';
import { server } from './server';

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' });
});

afterEach(() => {
  cleanup();
  resetViewport();
  server.resetHandlers();
});

afterAll(() => {
  server.close();
});

// jsdom does not implement the modal part of <dialog>.
if (typeof HTMLDialogElement !== 'undefined' && typeof HTMLDialogElement.prototype.showModal !== 'function') {
  HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  HTMLDialogElement.prototype.close = function close(this: HTMLDialogElement) {
    this.removeAttribute('open');
  };
}

// jsdom has no layout: media queries follow the width set by tests (see ./media.ts), and elements never resize.
window.matchMedia = (query: string) => mediaQueryList(query);
if (typeof window.ResizeObserver === 'undefined') {
  window.ResizeObserver = class {
    observe() {
      return undefined;
    }
    unobserve() {
      return undefined;
    }
    disconnect() {
      return undefined;
    }
  };
}
