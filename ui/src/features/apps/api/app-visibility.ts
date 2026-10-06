import { http } from '@/lib/api-client';

import type { App } from '../types';

/** The apps currently hidden from the dashboard. */
export const getHiddenApps = () => http.get<App[]>('/apps/hidden');

/** Hide or show an app on the dashboard. */
export const setAppVisibility = (name: string, hidden: boolean) =>
  http.put<void>(`/apps/${encodeURIComponent(name)}/visibility`, { hidden });
