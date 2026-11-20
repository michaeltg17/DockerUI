import { http } from '@/lib/api-client';

import type { Shortcut } from '../types';

export const getIcons = () => http.get<string[]>('/icons');

export const suggestIcon = (name: string, images: string[]) =>
  http.get<{ icon: string | null }>('/icons/suggest', {
    params: { name, image: images },
    // The api reads repeated 'image' params, not bracket-indexed ones.
    paramsSerializer: { indexes: null },
    silentError: true,
  });

export const addShortcut = (shortcut: Shortcut) =>
  http.post<Shortcut[]>('/shortcuts', shortcut);

export const updateShortcut = (name: string, shortcut: Shortcut) =>
  http.put<Shortcut[]>(`/shortcuts/${encodeURIComponent(name)}`, shortcut);

export const deleteShortcut = (name: string) =>
  http.delete<void>(`/shortcuts/${encodeURIComponent(name)}`);
