import { useMutation, useQueryClient } from '@tanstack/react-query';

import { rescanLan, scanLan } from '../api/lan';

import { APPS_QUERY_KEY } from './use-apps';
import { SETTINGS_QUERY_KEY } from './use-settings';

export const useScanLan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: scanLan,
    onSuccess: () => {
      // A scan changes shortcuts, which are merged into the apps feed.
      void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
      // The scanned subnet is saved, so the dialog's next prefill must see it.
      void queryClient.invalidateQueries({ queryKey: SETTINGS_QUERY_KEY });
    },
  });
};

export const useRescanLan = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (name: string) => rescanLan(name),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
    },
  });
};
