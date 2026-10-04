import { useQuery } from '@tanstack/react-query';

import { getLogs } from '../api/get-logs';

export const LOGS_QUERY_KEY = ['logs'] as const;

export const useLogs = (enabled: boolean) =>
  useQuery({
    queryKey: LOGS_QUERY_KEY,
    queryFn: getLogs,
    enabled,
    staleTime: 0,
  });
