import { useQuery } from '@tanstack/react-query';

import type { Logs } from '../api/get-logs';

export const useLogs = (
  enabled: boolean,
  scope: string,
  fetchLogs: () => Promise<Logs>,
) =>
  useQuery({
    queryKey: ['logs', scope],
    queryFn: fetchLogs,
    enabled,
    staleTime: 0,
  });
