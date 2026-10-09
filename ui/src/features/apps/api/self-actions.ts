import { api } from '@/lib/api-client';

// The dashboard goes down while this request is in flight, so the failure is
// expected and must not surface as an error toast.
export const restartSelf = () =>
  api.post<void, void, void>('/self/restart', undefined, { silentError: true });
