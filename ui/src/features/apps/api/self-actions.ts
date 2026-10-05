import { http } from '@/lib/api-client';

export const restartSelf = () => http.post<void>('/self/restart');

export const stopSelf = () => http.post<void>('/self/stop');

export const setSelfVisibility = (hidden: boolean) =>
  http.put<void>('/self/visibility', { hidden });
