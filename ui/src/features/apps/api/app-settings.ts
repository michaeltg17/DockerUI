import { http } from '@/lib/api-client';

export interface AppSettings {
  /** Display name override; empty or equal to the app name clears it. */
  displayName: string;
  /** Icon path override; empty clears it. */
  icon: string;
  /** Full url override; empty falls back to the resolved url. */
  url: string;
}

/** Persist the display name, icon, and url overrides of an app. */
export const setAppSettings = (name: string, settings: AppSettings) =>
  http.put<void>(`/apps/${encodeURIComponent(name)}/settings`, settings);
