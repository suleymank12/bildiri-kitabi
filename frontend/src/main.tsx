import '@fontsource-variable/source-serif-4';
import '@fontsource/ibm-plex-sans/400.css';
import '@fontsource/ibm-plex-sans/500.css';
import '@fontsource/ibm-plex-sans/600.css';
import '@fontsource/ibm-plex-mono/400.css';
import './styles/theme.css';

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './app/App';

const root = document.getElementById('root');
if (!root) {
  throw new Error('Root element #root is missing from index.html.');
}

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
