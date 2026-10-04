import { http } from '@/lib/api-client';

export type Logs = {
  available: boolean;
  logs: string;
};

export const getLogs = () => http.get<Logs>('/logs');
