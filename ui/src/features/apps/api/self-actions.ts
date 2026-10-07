import { http } from '@/lib/api-client';

export const restartSelf = () => http.post<void>('/self/restart');

export const stopSelf = () => http.post<void>('/self/stop');
