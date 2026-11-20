import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  addShortcut,
  deleteShortcut,
  getIcons,
  suggestIcon,
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

/**
 * The icon the api would auto-resolve for a name (and container images), using
 * the same catalogs as the app cards. Cached like the icon list: it only
 * changes when the name or the catalogs do.
 */
export const useIconSuggestion = (name: string, images: string[] = []) =>
  useQuery({
    queryKey: [...ICONS_QUERY_KEY, 'suggest', name.trim(), images],
    queryFn: () => suggestIcon(name.trim(), images),
    enabled: name.trim().length > 0,
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
