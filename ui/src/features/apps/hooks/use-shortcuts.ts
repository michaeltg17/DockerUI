import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  addShortcut,
  deleteShortcut,
  getIcons,
  updateShortcut,
} from '../api/shortcuts';
import type { Shortcut } from '../types';

import { APPS_QUERY_KEY } from './use-apps';

const ICONS_QUERY_KEY = ['icons'] as const;

/** Icon paths rarely change, so cache them for the whole session. */
export const useIcons = () =>
  useQuery({
    queryKey: ICONS_QUERY_KEY,
    queryFn: getIcons,
    staleTime: Infinity,
  });

const useShortcutMutation = <TVariables>(
  mutationFn: (variables: TVariables) => Promise<unknown>,
) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn,
    onSuccess: () => {
      // Shortcuts are merged into the apps feed, so refresh it. The SignalR
      // broadcast also pushes the change; refetching makes it immediate.
      void queryClient.invalidateQueries({ queryKey: APPS_QUERY_KEY });
    },
  });
};

export const useAddShortcut = () => useShortcutMutation(addShortcut);

export const useUpdateShortcut = () =>
  useShortcutMutation(
    ({ name, shortcut }: { name: string; shortcut: Shortcut }) =>
      updateShortcut(name, shortcut),
  );

export const useDeleteShortcut = () => useShortcutMutation(deleteShortcut);
