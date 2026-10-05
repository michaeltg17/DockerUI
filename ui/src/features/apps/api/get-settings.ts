import { http } from '@/lib/api-client';

export type Settings = {
  name: string;
};

export const getSettings = () => http.get<Settings>('/settings');
