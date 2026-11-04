import * as React from 'react';
import { createRoot } from 'react-dom/client';

import { applyTheme, getStoredTheme, PREFERS_DARK_QUERY } from '@/lib/theme';

import './index.css';
import { App } from './app';

applyTheme(getStoredTheme());

// If the stored preference is 'system', keep following the OS's color scheme
// when it changes while the page is open.
window.matchMedia(PREFERS_DARK_QUERY).addEventListener('change', () => {
  if (getStoredTheme() === 'system') applyTheme('system');
});

const root = document.getElementById('root');
if (!root) throw new Error('No root element found');

createRoot(root).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
