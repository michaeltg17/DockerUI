import { http } from '@/lib/api-client';

export type Logs = {
  available: boolean;
  logs: string;
};

export const getLogs = () => http.get<Logs>('/logs');

export const getAppLogs = (name: string) =>
  http
    .get<{ logs: string }>(`/apps/${encodeURIComponent(name)}/logs`)
    .then((result) => ({ available: true, logs: result.logs }));
