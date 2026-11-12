import { http } from '@/lib/api-client';

export interface LanScanResult {
  found: number;
  added: number;
}

/**
 * Runs a LAN scan over the given subnet; the dashboard scans its default
 * subnet when none is given. The effective subnet is saved for the next scan.
 */
export const scanLan = (subnet?: string) =>
  http.post<LanScanResult>(
    '/lan/scan',
    subnet === undefined ? undefined : { subnet },
  );

export const rescanLan = (name: string) =>
  http.post<void>(`/lan/rescan/${encodeURIComponent(name)}`);
