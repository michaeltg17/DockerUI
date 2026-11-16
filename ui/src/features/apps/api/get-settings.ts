import { http } from '@/lib/api-client';

export type Settings = {
  name: string;
  /** The compose project the dashboard itself runs in, or null when not in a container. */
  self: string | null;
  /** The LAN scan settings; the scan dialog prefills its field with the saved subnet. */
  lan?: { subnet?: string | null } | null;
};

export const getSettings = () => http.get<Settings>('/settings');

export const setDashboardName = (name: string) =>
  http.put('/settings/name', { name });
