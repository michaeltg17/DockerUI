import { useQuery } from '@tanstack/react-query';

import { getSettings } from '../api/get-settings';

export const SETTINGS_QUERY_KEY = ['settings'] as const;

export const useSettings = () =>
  useQuery({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: getSettings,
    staleTime: Infinity,
  });
