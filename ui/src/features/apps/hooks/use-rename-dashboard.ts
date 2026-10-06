import { useMutation, useQueryClient } from '@tanstack/react-query';

import { setDashboardName } from '../api/get-settings';

import { SETTINGS_QUERY_KEY } from './use-settings';

/**
 * Renames the dashboard. On success the settings query is refetched so the
 * page title (and anything else reading the settings) picks up the new name.
 */
export const useRenameDashboard = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (name: string) => setDashboardName(name),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: SETTINGS_QUERY_KEY });
    },
  });
};
