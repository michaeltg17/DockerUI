import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { restartApp, startApp, stopApp } from '../api/app-actions';
import { getApps } from '../api/get-apps';
import { setAppOrder } from '../api/set-app-order';

export const APPS_QUERY_KEY = ['apps'] as const;

export const useApps = () =>
  useQuery({
    queryKey: APPS_QUERY_KEY,
    queryFn: getApps,
  });

export const useStartApp = () => useAppActionMutation(startApp);
export const useStopApp = () => useAppActionMutation(stopApp);
export const useRestartApp = () => useAppActionMutation(restartApp);

export const useSetAppOrder = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: setAppOrder,
    onSuccess: () => {
      // The server will push the new order over SignalR as well;
      // refetching gives immediate, authoritative feedback.
      void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
    },
    onError: () => {
      // Pull the server's order back so the grid shows the truth.
      void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
    },
  });
};

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
