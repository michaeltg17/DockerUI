import { http } from '@/lib/api-client';

/** Split the given services out of a compose stack into their own cards. */
export const splitApp = (name: string, services: string[]) =>
  http.post<void>(`/apps/${encodeURIComponent(name)}/split`, { services });

/** Join a split part ('project-service') back into its stack. */
export const joinApp = (name: string) =>
  http.post<void>(`/apps/${encodeURIComponent(name)}/join`);
