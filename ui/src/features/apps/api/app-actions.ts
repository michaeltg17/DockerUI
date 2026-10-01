import { http } from '@/lib/api-client';

import type { App } from '../types';

type AppAction = 'start' | 'stop' | 'restart';

const performAction = (action: AppAction, name: string) =>
  http.post<App>(`/apps/${encodeURIComponent(name)}/${action}`);

export const startApp = (name: string) => performAction('start', name);

export const stopApp = (name: string) => performAction('stop', name);

export const restartApp = (name: string) => performAction('restart', name);
