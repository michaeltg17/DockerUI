import { http } from '@/lib/api-client';

/**
 * The user's first interaction with a 'new' app's card: the server clears the tag
 * and pushes the fresh app list to every client.
 */
export const markAppSeen = (name: string) =>
  http.post<void>(`/apps/${encodeURIComponent(name)}/seen`);
