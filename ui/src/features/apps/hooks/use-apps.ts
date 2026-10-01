import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { restartApp, startApp, stopApp } from '../api/app-actions';
import { getApps } from '../api/get-apps';

export const APPS_QUERY_KEY = ['apps'] as const;

export const useApps = () =>
  useQuery({
    queryKey: APPS_QUERY_KEY,
    queryFn: getApps,
  });

export const useStartApp = () => useAppActionMutation(startApp);
export const useStopApp = () => useAppActionMutation(stopApp);
export const useRestartApp = () => useAppActionMutation(restartApp);

function useAppActionMutation(action: (name: string) => Promise<unknown>) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: action,
    onSuccess: () => {
      // The server will push the new state over SignalR as well;
      // refetching gives immediate, authoritative feedback.
      void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
    },
  });
}
