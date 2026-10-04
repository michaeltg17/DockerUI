import { http } from '@/lib/api-client';

import type { Shortcut } from '../types';

export const getIcons = () => http.get<string[]>('/icons');

export const addShortcut = (shortcut: Shortcut) =>
  http.post<Shortcut[]>('/shortcuts', shortcut);

export const updateShortcut = (name: string, shortcut: Shortcut) =>
  http.put<Shortcut[]>(`/shortcuts/${encodeURIComponent(name)}`, shortcut);

export const deleteShortcut = (name: string) =>
  http.delete<void>(`/shortcuts/${encodeURIComponent(name)}`);
