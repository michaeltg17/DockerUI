import { http } from '@/lib/api-client';

export type Settings = {
  name: string;
  /** The compose project the dashboard itself runs in, or null when not in a container. */
  self: string | null;
};

export const getSettings = () => http.get<Settings>('/settings');

export const setDashboardName = (name: string) =>
  http.put('/settings/name', { name });
