import { http } from '@/lib/api-client';

export const restartSelf = () => http.post<void>('/self/restart');
