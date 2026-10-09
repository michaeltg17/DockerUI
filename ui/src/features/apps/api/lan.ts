import { http } from '@/lib/api-client';

export interface LanScanResult {
  found: number;
  added: number;
}

export const scanLan = () => http.post<LanScanResult>('/lan/scan');

export const rescanLan = (name: string) =>
  http.post<void>(`/lan/rescan/${encodeURIComponent(name)}`);
