import { http } from '@/lib/api-client';

export const setAppOrder = (order: string[]) =>
  http.put<void>('/apps/order', { order });
